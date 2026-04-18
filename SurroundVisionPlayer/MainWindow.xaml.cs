using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SurroundVisionPlayer.Logic;

namespace SurroundVisionPlayer;

public partial class MainWindow : Window
{
    // ── Data ─────────────────────────────────────────────────────────────────

    private SortedDictionary<string, Dictionary<string, string>> _recordings = [];
    private List<List<string>>  _sessions       = [];
    private List<string>        _allTimestamps  = [];
    private string?             _currentTs;
    private int                 _currentSession = -1;

    // ── Playback state ────────────────────────────────────────────────────────

    private string  _activeAngle = "FRONT";
    private bool    _isPlaying;
    private double  _playbackRate = 1.0;
    private bool    _sliderDragging;
    private bool    _suppressSlider;   // prevent feedback loop while ticking
    private int     _pendingOpens;     // counts MediaOpened callbacks still expected

    // ── Infrastructure ────────────────────────────────────────────────────────

    private readonly Dictionary<string, MediaElement>  _videos;
    private readonly Dictionary<string, ToggleButton>  _angleBtns;
    private readonly DispatcherTimer _syncTimer;

    // ── Constants ─────────────────────────────────────────────────────────────

    private static readonly TimeSpan StepSize      = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SyncThreshold = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TickInterval  = TimeSpan.FromMilliseconds(200);

    // ═════════════════════════════════════════════════════════════════════════
    // Construction
    // ═════════════════════════════════════════════════════════════════════════

    public MainWindow()
    {
        InitializeComponent();

        _videos = new Dictionary<string, MediaElement>
        {
            ["FRONT"] = VideoFront,
            ["LEFT"]  = VideoLeft,
            ["REAR"]  = VideoRear,
            ["RIGHT"] = VideoRight,
        };
        _angleBtns = new Dictionary<string, ToggleButton>
        {
            ["FRONT"] = BtnFront,
            ["LEFT"]  = BtnLeft,
            ["REAR"]  = BtnRear,
            ["RIGHT"] = BtnRight,
        };

        _syncTimer = new DispatcherTimer { Interval = TickInterval };
        _syncTimer.Tick += SyncTimer_Tick;
        _syncTimer.Start();

        // Try to auto-detect the SVR folder from the exe's parent directories
        var exeDir = AppContext.BaseDirectory;
        string? svr = null;
        foreach (var candidate in new[] { exeDir,
            Path.GetDirectoryName(exeDir) ?? exeDir,
            Path.GetDirectoryName(Path.GetDirectoryName(exeDir) ?? exeDir) ?? exeDir })
        {
            svr = RecordingScanner.FindSvrFolder(candidate);
            if (svr is not null) break;
        }

        if (svr is not null)
            LoadFolder(svr);
        else
            PromptForDrive();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Folder / data loading
    // ═════════════════════════════════════════════════════════════════════════

    private void LoadFolder(string svrFolder)
    {
        StopAll();
        _recordings     = RecordingScanner.Scan(svrFolder);
        _allTimestamps  = [.. _recordings.Keys];
        _sessions       = SessionGrouper.Group(_allTimestamps);

        PopulateSessionList();

        int n = _sessions.Count, c = _allTimestamps.Count;
        CountLabel.Text = $"{n} trip(s), {c} clips";

        if (_allTimestamps.Count > 0)
        {
            SessionList.SelectedIndex = 0;   // triggers SelectionChanged → LoadRecording
        }
        else
        {
            StatusLabel.Text = $"No recordings found in: {svrFolder}";
        }
    }

    private void PopulateSessionList()
    {
        SessionList.SelectionChanged -= SessionList_SelectionChanged;
        SessionList.Items.Clear();

        string? prevDate = null;
        foreach (var session in _sessions)
        {
            var dt = SessionGrouper.ParseTimestamp(session[0]);
            var dateStr = dt.ToString("yyyy-MM-dd");

            if (dateStr != prevDate)
            {
                prevDate = dateStr;
                // Date header (not selectable)
                SessionList.Items.Add(new SessionListItem
                {
                    Label        = $"─── {dateStr} ───",
                    IsHeader     = true,
                    SessionIndex = -1,
                    FirstTs      = string.Empty,
                });
            }

            var badges = string.Concat(RecordingScanner.Angles
                .Where(a => session.Any(ts => _recordings.TryGetValue(ts, out var f) && f.ContainsKey(a)))
                .Select(a => $"[{a[0]}]"));

            SessionList.Items.Add(new SessionListItem
            {
                Label        = $"  {SessionGrouper.Label(session)}  {badges}",
                IsHeader     = false,
                SessionIndex = _sessions.IndexOf(session),
                FirstTs      = session[0],
                Session      = session,
            });
        }

        SessionList.SelectionChanged += SessionList_SelectionChanged;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Recording loading
    // ═════════════════════════════════════════════════════════════════════════

    private void LoadRecording(string ts, int sessionIdx)
    {
        StopAll();
        _currentTs      = ts;
        _currentSession = sessionIdx;
        _pendingOpens   = 0;

        var files = _recordings[ts];
        foreach (var (angle, me) in _videos)
        {
            if (files.TryGetValue(angle, out var path))
            {
                _pendingOpens++;
                me.Source = new Uri(path);
            }
            else
            {
                me.Source = null;
            }
        }

        ResetSlider();
        UpdateStatus();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Playback control
    // ═════════════════════════════════════════════════════════════════════════

    private void PlayAll()
    {
        foreach (var me in _videos.Values)
            if (me.Source is not null)
                me.Play();
        _isPlaying = true;
        BtnPlay.Content = "⏸";
    }

    private void PauseAll()
    {
        foreach (var me in _videos.Values)
            me.Pause();
        _isPlaying = false;
        BtnPlay.Content = "▶";
    }

    private void StopAll()
    {
        foreach (var me in _videos.Values)
            me.Stop();
        _isPlaying = false;
        BtnPlay.Content = "▶";
    }

    private void SeekAll(TimeSpan pos)
    {
        pos = pos < TimeSpan.Zero ? TimeSpan.Zero : pos;
        foreach (var me in _videos.Values)
            if (me.Source is not null)
                me.Position = pos;
    }

    private void SetRate(double rate)
    {
        _playbackRate = rate;
        foreach (var me in _videos.Values)
            me.SpeedRatio = rate;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Angle switching
    // ═════════════════════════════════════════════════════════════════════════

    private void SwitchAngle(string angle)
    {
        if (angle == _activeAngle) return;

        // Grab current position for sync before switching
        var refPos = _videos[_activeAngle].Position;

        _activeAngle = angle;
        AngleLabel.Text = angle;

        foreach (var (a, me) in _videos)
        {
            me.Visibility = a == angle ? Visibility.Visible : Visibility.Hidden;
            me.IsMuted    = a != angle;
        }
        foreach (var (a, btn) in _angleBtns)
            btn.IsChecked = a == angle;

        // Nudge newly active player to reference position if it drifted
        if (refPos > TimeSpan.Zero && _videos[angle].Source is not null)
            _videos[angle].Position = refPos;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Auto-advance
    // ═════════════════════════════════════════════════════════════════════════

    private void AdvanceToNextClip()
    {
        if (_currentTs is null || _currentSession < 0) return;
        var session = _sessions[_currentSession];
        var idx = session.IndexOf(_currentTs);
        if (idx < 0 || idx + 1 >= session.Count)
        {
            StopAll();
            StatusLabel.Text = "End of trip.";
            return;
        }
        LoadRecording(session[idx + 1], _currentSession);
        PlayAll();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Sync timer tick
    // ═════════════════════════════════════════════════════════════════════════

    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentTs is null) return;

        var active = _videos[_activeAngle];
        var pos    = active.Position;
        var natDur = active.NaturalDuration;

        // Update seek slider
        if (!_sliderDragging && natDur.HasTimeSpan && natDur.TimeSpan > TimeSpan.Zero)
        {
            _suppressSlider = true;
            SeekSlider.Value = pos.TotalMilliseconds / natDur.TimeSpan.TotalMilliseconds * 10000.0;
            _suppressSlider = false;
        }
        TimeLabel.Text = FormatTime(pos);
        DurLabel.Text  = natDur.HasTimeSpan ? FormatTime(natDur.TimeSpan) : "?:??";

        // Drift correction for non-active players
        if (_isPlaying && pos > TimeSpan.Zero)
        {
            foreach (var (angle, me) in _videos)
            {
                if (angle == _activeAngle || me.Source is null) continue;
                var drift = (me.Position - pos).Duration();
                if (drift > SyncThreshold)
                    me.Position = pos;
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MediaElement events
    // ═════════════════════════════════════════════════════════════════════════

    private void Media_Opened(object sender, RoutedEventArgs e)
    {
        _pendingOpens = Math.Max(0, _pendingOpens - 1);

        // Apply rate (rate must be set after media opens)
        if (sender is MediaElement me)
            me.SpeedRatio = _playbackRate;

        // Once all requested angles have opened, auto-play if we were playing
        if (_pendingOpens == 0 && _isPlaying)
            PlayAll();
    }

    private void Media_Ended(object sender, RoutedEventArgs e)
    {
        // Only react to the active angle's end-of-media event
        if (sender is MediaElement me && me == _videos[_activeAngle])
            AdvanceToNextClip();
    }

    private void Media_Failed(object sender, ExceptionRoutedEventArgs e)
    {
        StatusLabel.Text = $"Media error: {e.ErrorException?.Message}";
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UI event handlers
    // ═════════════════════════════════════════════════════════════════════════

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionListItem item || item.IsHeader)
            return;
        if (item.FirstTs == _currentTs && item.SessionIndex == _currentSession)
            return;

        bool wasPlaying = _isPlaying;
        LoadRecording(item.FirstTs, item.SessionIndex);
        if (wasPlaying) PlayAll();
    }

    private void Angle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn && btn.Tag is string angle)
        {
            // Prevent un-checking the active button
            if (angle == _activeAngle)
            {
                btn.IsChecked = true;
                return;
            }
            SwitchAngle(angle);
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying) PauseAll(); else PlayAll();
    }

    private void Beginning_Click(object sender, RoutedEventArgs e) => SeekAll(TimeSpan.Zero);
    private void End_Click(object sender, RoutedEventArgs e)
    {
        var dur = _videos[_activeAngle].NaturalDuration;
        if (dur.HasTimeSpan)
            SeekAll(dur.TimeSpan - TimeSpan.FromMilliseconds(200));
    }
    private void StepBack_Click(object sender, RoutedEventArgs e)
        => SeekAll(_videos[_activeAngle].Position - StepSize);
    private void StepFwd_Click(object sender, RoutedEventArgs e)
        => SeekAll(_videos[_activeAngle].Position + StepSize);

    private void HalfSpeed_Click(object sender, RoutedEventArgs e)   => SetRate(0.5);
    private void NormalSpeed_Click(object sender, RoutedEventArgs e)  => SetRate(1.0);
    private void DoubleSpeed_Click(object sender, RoutedEventArgs e)  => SetRate(2.0);

    // Seek slider
    private void SeekSlider_MouseDown(object sender, MouseButtonEventArgs e)
        => _sliderDragging = true;

    private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _sliderDragging = false;
        var dur = _videos[_activeAngle].NaturalDuration;
        if (!dur.HasTimeSpan) return;
        SeekAll(TimeSpan.FromMilliseconds(
            SeekSlider.Value / 10000.0 * dur.TimeSpan.TotalMilliseconds));
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Only seek during drag (not during programmatic tick updates)
        if (_suppressSlider || !_sliderDragging) return;
        var dur = _videos[_activeAngle].NaturalDuration;
        if (!dur.HasTimeSpan) return;
        SeekAll(TimeSpan.FromMilliseconds(
            SeekSlider.Value / 10000.0 * dur.TimeSpan.TotalMilliseconds));
    }

    // Menu
    private void OpenDrive_Click(object sender, RoutedEventArgs e) => PromptForDrive();
    private void Quit_Click(object sender, RoutedEventArgs e)       => Close();

    // Keyboard shortcuts
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                if (_isPlaying) PauseAll(); else PlayAll();
                e.Handled = true; break;
            case Key.Left:
                SeekAll(_videos[_activeAngle].Position - StepSize);
                e.Handled = true; break;
            case Key.Right:
                SeekAll(_videos[_activeAngle].Position + StepSize);
                e.Handled = true; break;
            case Key.F: SwitchAngle("FRONT"); e.Handled = true; break;
            case Key.L: SwitchAngle("LEFT");  e.Handled = true; break;
            case Key.R: SwitchAngle("REAR");  e.Handled = true; break;
            case Key.G: SwitchAngle("RIGHT"); e.Handled = true; break;
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _syncTimer.Stop();
        StopAll();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Drive picker
    // ═════════════════════════════════════════════════════════════════════════

    private void PromptForDrive()
    {
        var dlg = new DrivePickerWindow
        {
            Owner       = IsLoaded ? this : null,
            InitialPath = _recordings.Count > 0
                ? Path.GetDirectoryName(
                    Path.GetDirectoryName(
                        Path.GetDirectoryName(
                            _recordings.Values.FirstOrDefault()?.Values.FirstOrDefault())))
                : null,
        };

        if (dlg.ShowDialog() == true && dlg.SelectedFolder is not null)
            LoadFolder(dlg.SelectedFolder);
        else if (_recordings.Count == 0)
            Close();   // Nothing loaded and user cancelled — exit
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
    }

    private void ResetSlider()
    {
        _suppressSlider = true;
        SeekSlider.Value = 0;
        _suppressSlider = false;
        TimeLabel.Text = "0:00";
        DurLabel.Text  = "?:??";
    }

    private void UpdateStatus()
    {
        if (_currentTs is null) return;
        var dt    = SessionGrouper.ParseTimestamp(_currentTs);
        var files = _recordings[_currentTs];
        var have  = string.Join(", ", RecordingScanner.Angles.Where(a => files.ContainsKey(a)));
        var session = _currentSession >= 0 ? _sessions[_currentSession] : null;
        var clipInfo = session is not null
            ? $"Clip {session.IndexOf(_currentTs) + 1}/{session.Count}"
            : "";
        StatusLabel.Text =
            $"{dt:yyyy-MM-dd HH:mm:ss}  |  Angles: {have}  |  {clipInfo}";
    }
}

// ── List data model ───────────────────────────────────────────────────────────

internal sealed class SessionListItem
{
    public required string       Label        { get; init; }
    public required bool         IsHeader     { get; init; }
    public required int          SessionIndex { get; init; }
    public required string       FirstTs      { get; init; }
    public List<string>?         Session      { get; init; }
}
