namespace SpotifyLyricsWin.Player;

/// <summary>一次 Observe 的处理结果（供跟踪器限频写诊断日志、供无头测试断言）。</summary>
public enum ClockAction
{
    /// <summary>首个样本 / 切歌 / 播放状态变化：直接锚定到样本。</summary>
    Snapped,
    /// <summary>偏差在微调区（≤300ms）：平滑吸收（比例控制，含速率补偿）。</summary>
    MicroAdjust,
    /// <summary>中偏差（300ms~2500ms）：速率补偿收敛中，不硬跳、不倒退。</summary>
    Compensating,
    /// <summary>偏差 >2500ms：视为 seek，硬跳到样本。</summary>
    SeekSnap,
    /// <summary>v0.6：0 样本防护命中，样本被忽略（见 ProgressClock.Observe）。</summary>
    IgnoredZero
}

/// <summary>
/// 显示进度时钟（v0.5 新增，复刻安卓 V2.4 播放时钟的 observe 设计意图）。
///
/// 背景：v0.4 及以前 SmtcTracker 用「死区」逻辑——同曲目时外推与 SMTC 报告差
/// &lt;1500ms 就丢弃样本、攒到阈值再硬跳，用户实测逐字扫光「跑快了又回退、跟唱对不上」。
/// 安卓版没有死区：每个样本都交给时钟 observe() 内部消化校正。本类即 observe 语义：
/// - 每个 SMTC 样本必过 <see cref="Observe"/>：样本位置对应的是时间线 LastUpdatedTime
///   时刻的进度（由调用方换算），不是轮询收到时刻；
/// - 时钟内部维护「显示锚点 + 速率倍率」：显示进度 = 锚点 + 流逝 × 倍率；
/// - 偏差 dev = 样本折算到当前时刻的进度 − 当前显示进度：
///   |dev| &gt; 2500ms → 视为 seek 硬跳；播放状态变化 / 首样本 → 锚定；
///   其余一律走比例控制：倍率 = 1 + clamp(dev / 900ms, −0.30, +0.60)，
///   偏差越小修正越轻（|dev|≤135ms 时倍率偏离 ≤0.15），大偏差在约 2 秒内指数收敛；
/// - 播放中 PositionMs 单调不减（内部钳制；只有 Observe 的硬跳/锚定能改基准）；
/// - 暂停时显示进度冻结在样本位置（暂停 seek 同样直接锚定）。
/// 时间源由构造注入（生产 = TrackInfo.NowMs 单调时钟；测试 = 假时钟），纯逻辑可无头测。
/// </summary>
public sealed class ProgressClock
{
    /// <summary>偏差超过此值视为 seek（硬跳）。</summary>
    public const long SeekThresholdMs = 2500;
    /// <summary>微调/补偿分界（仅用于动作分类与日志，不改变算法）。</summary>
    public const long MicroThresholdMs = 300;
    /// <summary>比例控制时间常数：倍率修正量 = dev / TauMs（再钳制）。</summary>
    private const double TauMs = 900.0;
    private const double MinCorrection = -0.45;  // 最慢 0.55×：样本落后时放慢等它追上，显示不倒退
    private const double MaxCorrection = 0.60;   // 最快 1.60×：样本超前时加速追赶

    private readonly Func<long> _nowMs;

    private bool _hasSample;
    private bool _playing;
    private long _anchorPosMs;    // 显示锚点位置
    private long _anchorTimeMs;   // 锚点对应的时钟时刻
    private double _rate = 1.0;   // 当前外推倍率
    private long _durationMs;
    private long _lastReturned;   // 单调钳制基准（播放中 PositionMs 不低于它）

    /// <summary>最近一次 Observe 算出的偏差（样本 − 显示，毫秒；正 = 显示慢了）。</summary>
    public long LastDeviationMs { get; private set; }
    /// <summary>最近一次 Observe 的动作分类。</summary>
    public ClockAction LastAction { get; private set; } = ClockAction.Snapped;
    /// <summary>当前外推倍率（诊断/测试用）。</summary>
    public double CurrentRate => _rate;

    public ProgressClock(Func<long> nowMsSource) => _nowMs = nowMsSource;

    /// <summary>切歌/会话消失时调用：回到无样本状态。</summary>
    public void Reset()
    {
        _hasSample = false;
        _playing = false;
        _rate = 1.0;
        _lastReturned = 0;
        LastDeviationMs = 0;
    }

    /// <summary>
    /// 喂入一个 SMTC 样本。samplePosMs 是时间线在 sampleAtMs（LastUpdatedTime 换算到
    /// 单调时钟域）时刻的播放位置。每个样本都必须调用本方法，不允许调用方预筛丢弃。
    /// </summary>
    public ClockAction Observe(long samplePosMs, long sampleAtMs, bool isPlaying, long durationMs)
    {
        long now = _nowMs();

        // v0.6：0 样本防护（借鉴 LyricFlow PlaybackClock 条件式 `posMs<=0 && anchorPos>1500` 才忽略）。
        // 播放中段 SMTC 偶发给出 Position=0（如广告切换/会话重建），若照单全收，
        // 偏差 >2500ms 会被当成 seek 硬跳，把歌词拉回开头。锚点还在开头 1.5s 内时 0 是
        // 合法值（真开头），不忽略。仅播放中生效：暂停样本是权威值（暂停 seek 到 0 必须认）。
        // 代价：单曲循环回到 0 的那一个样本会被忽略，靠后续 >0 的样本按 seek 拉回（已被时长钳制兜住）。
        if (isPlaying && samplePosMs <= 0 && _hasSample && RawPosition(now) > 1500)
        {
            LastDeviationMs = 0;
            return LastAction = ClockAction.IgnoredZero;
        }

        _durationMs = durationMs;
        // 样本折算到「现在」：播放中位置随时间走，暂停时就停在样本位置
        long sampleNow = isPlaying ? samplePosMs + Math.Max(0, now - sampleAtMs) : samplePosMs;
        if (sampleNow < 0) sampleNow = 0;
        if (durationMs > 0) sampleNow = Math.Min(sampleNow, durationMs);

        if (!_hasSample || isPlaying != _playing)
        {
            Snap(sampleNow, now, isPlaying);
            LastDeviationMs = 0;
            return LastAction = ClockAction.Snapped;
        }

        if (!isPlaying)
        {
            // 暂停中样本即权威（暂停 seek 也走这里）：直接锚定，不外推
            Snap(sampleNow, now, isPlaying: false);
            LastDeviationMs = 0;
            return LastAction = ClockAction.Snapped;
        }

        long dispNow = RawPosition(now);
        long dev = sampleNow - dispNow;
        LastDeviationMs = dev;

        if (Math.Abs(dev) > SeekThresholdMs)
        {
            Snap(sampleNow, now, isPlaying: true);
            return LastAction = ClockAction.SeekSnap;
        }

        // 比例控制：以当前显示位置为新锚点，按偏差大小设倍率，
        // 显示轨迹连续（不跳变）、单调不减，偏差指数收敛（τ≈900ms）。
        _anchorPosMs = dispNow;
        _anchorTimeMs = now;
        double correction = Math.Clamp(dev / TauMs, MinCorrection, MaxCorrection);
        _rate = 1.0 + correction;
        return LastAction = Math.Abs(dev) <= MicroThresholdMs
            ? ClockAction.MicroAdjust
            : ClockAction.Compensating;
    }

    private void Snap(long posMs, long now, bool isPlaying)
    {
        _anchorPosMs = posMs;
        _anchorTimeMs = now;
        _rate = 1.0;
        _playing = isPlaying;
        _hasSample = true;
        _lastReturned = posMs;
    }

    private long RawPosition(long now)
    {
        if (!_playing) return _anchorPosMs;
        double p = _anchorPosMs + (now - _anchorTimeMs) * _rate;
        long v = (long)p;
        if (v < 0) v = 0;
        if (_durationMs > 0) v = Math.Min(v, _durationMs);
        return v;
    }

    /// <summary>当前显示进度（毫秒）。无样本返回 -1。</summary>
    public long PositionMs() => PositionMs(_nowMs());

    /// <summary>指定时刻的显示进度（测试用）。播放中单调不减（硬跳改基准除外）。</summary>
    public long PositionMs(long now)
    {
        if (!_hasSample) return -1;
        long v = RawPosition(now);
        if (_playing)
        {
            if (v < _lastReturned) v = _lastReturned;  // 单调钳制：补偿期间绝不倒退
            _lastReturned = v;
        }
        else
        {
            _lastReturned = v;
        }
        return v;
    }
}
