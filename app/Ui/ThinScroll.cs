using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Xml;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 自绘细滚动条（v0.5，用户点名）：宽/高 7~8px、轨道透明、滑块圆角半透明白灰、
/// 悬停/拖动时加深；内容不溢出时不显示（Computed*Visibility 驱动）。
/// 实现路线：ScrollBar/ScrollViewer 的模板含 Track/RepeatButton 等子部件，
/// FrameworkElementFactory 在代码里装配不可靠（v0.3 滑杆踩过），改用 XamlReader
/// 加载模板 XAML 字符串——这是本项目唯一一处运行时 XAML，加载失败自动回退系统
/// 滚动条（功能不受影响，Theme 会跳过注册），不会让窗口打不开。
/// </summary>
public static class ThinScroll
{
    private const string DictXaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="ScrollBar">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                  <Grid Background="Transparent">
                    <Track x:Name="PART_Track"
                           Minimum="{TemplateBinding Minimum}"
                           Maximum="{TemplateBinding Maximum}"
                           Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}}"
                           ViewportSize="{TemplateBinding ViewportSize}"
                           IsDirectionReversed="True">
                      <Track.DecreaseRepeatButton>
                        <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/>
                      </Track.DecreaseRepeatButton>
                      <Track.IncreaseRepeatButton>
                        <RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/>
                      </Track.IncreaseRepeatButton>
                      <Track.Thumb>
                        <Thumb>
                          <Thumb.Template>
                            <ControlTemplate TargetType="Thumb">
                              <Border x:Name="ThumbBd" CornerRadius="3" Margin="1,1,1,1" Background="#59FFFFFF"/>
                              <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True">
                                  <Setter TargetName="ThumbBd" Property="Background" Value="#A6FFFFFF"/>
                                </Trigger>
                                <Trigger Property="IsDragging" Value="True">
                                  <Setter TargetName="ThumbBd" Property="Background" Value="#D9FFFFFF"/>
                                </Trigger>
                              </ControlTemplate.Triggers>
                            </ControlTemplate>
                          </Thumb.Template>
                        </Thumb>
                      </Track.Thumb>
                    </Track>
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="Orientation" Value="Horizontal">
                      <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
            <Style.Triggers>
              <Trigger Property="Orientation" Value="Vertical">
                <Setter Property="Width" Value="8"/>
                <Setter Property="MinWidth" Value="8"/>
              </Trigger>
              <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Height" Value="8"/>
                <Setter Property="MinHeight" Value="8"/>
              </Trigger>
            </Style.Triggers>
          </Style>

          <Style TargetType="ScrollViewer">
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ScrollViewer">
                  <Grid Background="{TemplateBinding Background}">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="*"/>
                      <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                      <RowDefinition Height="*"/>
                      <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <ScrollContentPresenter Grid.Column="0" Grid.Row="0"
                                            Margin="{TemplateBinding Padding}"
                                            ContentTemplate="{TemplateBinding ContentTemplate}"
                                            CanContentScroll="{TemplateBinding CanContentScroll}"/>
                    <ScrollBar x:Name="PART_VerticalScrollBar" Grid.Column="1" Grid.Row="0"
                               Orientation="Vertical" Minimum="0"
                               Maximum="{TemplateBinding ScrollableHeight}"
                               ViewportSize="{TemplateBinding ViewportHeight}"
                               Value="{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}}"
                               Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}"/>
                    <ScrollBar x:Name="PART_HorizontalScrollBar" Grid.Column="0" Grid.Row="1"
                               Orientation="Horizontal" Minimum="0"
                               Maximum="{TemplateBinding ScrollableWidth}"
                               ViewportSize="{TemplateBinding ViewportWidth}"
                               Value="{Binding HorizontalOffset, RelativeSource={RelativeSource TemplatedParent}}"
                               Visibility="{TemplateBinding ComputedHorizontalScrollBarVisibility}"/>
                  </Grid>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """;

    private static ResourceDictionary? _cached;
    private static bool _tried;

    /// <summary>加载细滚动条资源字典；失败返回 null（调用方跳过，即回退系统滚动条）。</summary>
    public static ResourceDictionary? Load()
    {
        if (_tried) return _cached;
        _tried = true;
        try
        {
            using var sr = new StringReader(DictXaml);
            using var xr = XmlReader.Create(sr);
            _cached = (ResourceDictionary)XamlReader.Load(xr);
        }
        catch
        {
            _cached = null; // 模板装配失败不许连累窗口打开：回退系统滚动条
        }
        return _cached;
    }
}
