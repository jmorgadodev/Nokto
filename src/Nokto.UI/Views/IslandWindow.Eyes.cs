using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Input;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    /// <summary>Interacciones cortas; los ojos minimales nunca arrancan temporizadores.</summary>
    private void ConfigureEyeMotion()
    {
        if (_settings is null) return;
        bool eyes = _islandReady && !EyeStyleCatalog.IsMinimal(_settings.SelectedStyle) && !_settings.ReducedMotion;
        if (!eyes)
        {
            _cursorTimer?.Stop();
            _blinkTimer?.Stop();
            _eyeFrameTimer?.Stop();
            _gazeAnimating = _blinking = false;
            _gazeX = _gazeY = 0;
            _sleeping = false;
            EyesVisual.SetPose(0, 0, 0, false);
            return;
        }

        if (_settings.FollowCursor)
        {
            if (_cursorTimer is not null) _cursorTimer.Interval = TimeSpan.FromMilliseconds(80);
            _cursorTimer?.Start();
            SampleCursor();
        }
        else
        {
            _cursorTimer?.Stop();
            _gazeX = _gazeY = 0;
            EyesVisual.SetPose(0, 0, 0, false);
        }
        if (_settings.NaturalBlink)
        {
            if (_blinkTimer?.IsEnabled == false)
            {
                _blinkTimer.Interval = TimeSpan.FromMilliseconds(Random.Shared.Next(3800, 8500));
                _blinkTimer.Start();
            }
        }
        else
        {
            _blinkTimer?.Stop();
            _blinking = false;
        }
    }

    private DispatcherTimer? _demoTimer;
    private int _demoPhase;

    private void OnTestEyes(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_settings is null || _settings.ReducedMotion || EyeStyleCatalog.IsMinimal(_settings.SelectedStyle)) return;
        _demoTimer?.Stop();
        _demoPhase = 0;
        _demoTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _demoTimer.Tick -= OnDemoTick;
        _demoTimer.Tick += OnDemoTick;
        _demoTimer.Start();
        MoveGaze(-1, -.20);
    }

    private void OnDemoTick(object? sender, EventArgs e)
    {
        _demoPhase++;
        switch (_demoPhase)
        {
            case 1: MoveGaze(1, .26); break;
            case 2: MoveGaze(0, -1); break;
            case 3:
                _blinking = true;
                _blinkStart = Stopwatch.GetTimestamp();
                _eyeFrameTimer?.Start();
                MoveGaze(0, 0);
                break;
            default:
                _demoTimer?.Stop();
                SampleCursor();
                break;
        }
    }

    private void MoveGaze(double x, double y)
    {
        _gazeFromX = _gazeX;
        _gazeFromY = _gazeY;
        _gazeTargetX = Math.Clamp(x, -1, 1);
        _gazeTargetY = Math.Clamp(y, -1, 1);
        _gazeStart = Stopwatch.GetTimestamp();
        _sleeping = false;
        _gazeAnimating = true;
        _eyeFrameTimer?.Start();
    }

    private void SampleCursor()
    {
        if (_settings?.FollowCursor != true || _settings.ReducedMotion || !_islandReady || _demoTimer?.IsEnabled == true) return;
        if (!CursorPositionReader.TryGet(out int cursorX, out int cursorY)) return;

        bool changed = _lastCursorX == int.MinValue ||
            Math.Abs(cursorX - _lastCursorX) > 2 || Math.Abs(cursorY - _lastCursorY) > 2;
        if (changed)
        {
            _lastCursorX = cursorX;
            _lastCursorY = cursorY;
            _lastCursorMovement = DateTime.UtcNow;
            _cursorTimer!.Interval = TimeSpan.FromMilliseconds(80);
            _sleeping = false;
            double scale = Screens.Primary?.Scaling ?? 1;
            double midX = Position.X + Width * scale * .5;
            double midY = Position.Y + 14 * scale;
            double sensitivity = Math.Clamp(_settings.EyeSensitivity, 0, 1);
            double desiredX = Math.Tanh((cursorX - midX) / 285) * (0.65 + sensitivity * .35);
            double desiredY = Math.Tanh((cursorY - midY) / 245) * (0.65 + sensitivity * .35);
            if (Math.Abs(desiredX - _gazeTargetX) > .03 || Math.Abs(desiredY - _gazeTargetY) > .03)
            {
                MoveGaze(desiredX, desiredY);
            }
        }
        else if (_settings.SleepOnIdle && !_sleeping &&
                 (DateTime.UtcNow - _lastCursorMovement).TotalSeconds >= 60)
        {
            _sleeping = true;
            _cursorTimer!.Interval = TimeSpan.FromMilliseconds(650);
            _eyeFrameTimer?.Stop();
            _gazeAnimating = _blinking = false;
            EyesVisual.SetPose(_gazeX, _gazeY, 0, true);
        }
    }

    private void StartBlink()
    {
        _blinkTimer?.Stop();
        if (_settings?.NaturalBlink != true || _settings.ReducedMotion || !_islandReady ||
            EyeStyleCatalog.IsMinimal(_settings.SelectedStyle)) return;
        if (!_sleeping)
        {
            _blinking = true;
            _blinkStart = Stopwatch.GetTimestamp();
            _eyeFrameTimer?.Start();
        }
        if (_blinkTimer is not null)
        {
            _blinkTimer.Interval = TimeSpan.FromMilliseconds(Random.Shared.Next(2900, 6200));
            _blinkTimer.Start();
        }
    }

    private void AdvanceEyeFrame()
    {
        if (_settings is null || _settings.ReducedMotion || !_islandReady)
        {
            _eyeFrameTimer?.Stop();
            return;
        }

        if (_gazeAnimating)
        {
            double t = Math.Clamp(Stopwatch.GetElapsedTime(_gazeStart).TotalSeconds / .22, 0, 1);
            double smooth = 1 - Math.Pow(1 - t, 3);
            _gazeX = _gazeFromX + (_gazeTargetX - _gazeFromX) * smooth;
            _gazeY = _gazeFromY + (_gazeTargetY - _gazeFromY) * smooth;
            if (t >= 1) _gazeAnimating = false;
        }

        double blink = 0;
        if (_blinking)
        {
            double t = Math.Clamp(Stopwatch.GetElapsedTime(_blinkStart).TotalSeconds / .19, 0, 1);
            blink = t <= .45 ? t / .45 : (1 - t) / .55;
            if (t >= 1) _blinking = false;
        }
        EyesVisual.SetPose(_gazeX, _gazeY, blink, _sleeping);
        if (!_gazeAnimating && !_blinking) _eyeFrameTimer?.Stop();
    }
}
