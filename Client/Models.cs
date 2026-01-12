using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace subphimv1.Subphim
{
    public class SrtSubtitleLine : INotifyPropertyChanged
    {
        private int _index;
        private string _timeCode;
        private string _originalText;
        private string _translatedText;
        private int _trackIndex = 0;
        private TimeSpan _startTime;
        private TimeSpan _duration;
        private bool _isVoiced;
        private string _voicedAudioPath;
        private TimeSpan RoundToMilliseconds(TimeSpan time)
        {
            return TimeSpan.FromMilliseconds(Math.Round(time.TotalMilliseconds));
        }
        public string OwnerVideoPath { get; set; }
        public Models.StyleState Style { get; set; }
        public bool IsTextClip { get; set; } = false;
        private string _assOverrideOriginal;
        public string AssOverrideOriginal
        {
            get => _assOverrideOriginal;
            set
            {
                if (_assOverrideOriginal != value)
                {
                    _assOverrideOriginal = value;
                    OnPropertyChanged(nameof(AssOverrideOriginal));
                }
            }
        }

        private string _assOverrideTranslated;
        public string AssOverrideTranslated
        {
            get => _assOverrideTranslated;
            set
            {
                if (_assOverrideTranslated != value)
                {
                    _assOverrideTranslated = value;
                    OnPropertyChanged(nameof(AssOverrideTranslated));
                }
            }
        }

        private bool _hasManualAssOverride;
        public bool HasManualAssOverride
        {
            get => _hasManualAssOverride;
            set
            {
                if (_hasManualAssOverride != value)
                {
                    _hasManualAssOverride = value;
                    OnPropertyChanged(nameof(HasManualAssOverride));
                }
            }
        }

        /// <summary>Xóa override (ví dụ khi người dùng đổi text nhiều).</summary>
        public void ClearAssOverride()
        {
            _assOverrideOriginal = null;
            _assOverrideTranslated = null;
            _hasManualAssOverride = false;
            OnPropertyChanged(nameof(AssOverrideOriginal));
            OnPropertyChanged(nameof(AssOverrideTranslated));
            OnPropertyChanged(nameof(HasManualAssOverride));
        }
        public int Index
        {
            get => _index;
            set { if (_index != value) { _index = value; OnPropertyChanged(nameof(Index)); } }
        }
        public string TimeCode
        {
            get => _timeCode;
            private set { if (_timeCode != value) { _timeCode = value; OnPropertyChanged(nameof(TimeCode)); } }
        }
        public string OriginalText
        {
            get => _originalText;
            set { if (_originalText != value) { _originalText = value; OnPropertyChanged(nameof(OriginalText)); } }
        }
        public string TranslatedText
        {
            get => _translatedText;
            set { if (_translatedText != value) { _translatedText = value; OnPropertyChanged(nameof(TranslatedText)); } }
        }
        public int TrackIndex
        {
            get => _trackIndex;
            set { if (_trackIndex != value) { _trackIndex = value; OnPropertyChanged(nameof(TrackIndex)); } }
        }
        public string ImagePath { get; set; }
        public TimeSpan StartTime
        {
            get => _startTime;
            set
            {
                var roundedValue = RoundToMilliseconds(value);
                if (_startTime != roundedValue)
                {
                    _startTime = roundedValue;
                    OnPropertyChanged(nameof(StartTime));
                    OnPropertyChanged(nameof(EndTime));
                    UpdateTimeCodeProperty();
                }
            }
        }
        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                var roundedValue = RoundToMilliseconds(value);
                if (roundedValue < TimeSpan.Zero) roundedValue = TimeSpan.Zero;

                if (_duration != roundedValue)
                {
                    _duration = roundedValue;
                    OnPropertyChanged(nameof(Duration));
                    OnPropertyChanged(nameof(EndTime));
                    UpdateTimeCodeProperty();
                }
            }
        }

        public TimeSpan EndTime
        {
            get => StartTime + Duration;
            set
            {
                var newDuration = (value > StartTime) ? (value - StartTime) : TimeSpan.Zero;
                Duration = newDuration;
            }
        }

        public void SetTimeFromTimeCodeString(string timeCodeString)
        {
            if (string.IsNullOrWhiteSpace(timeCodeString))
            {
                _startTime = TimeSpan.Zero;
                _duration = TimeSpan.FromSeconds(1);
                UpdateTimeCodeProperty();
                return;
            }

            var parts = timeCodeString.Split(new[] { " --> " }, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                TimeSpan parsedStart, parsedEnd;
                TimeSpan.TryParseExact(parts[0].Replace(',', '.'), @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out parsedStart);
                TimeSpan.TryParseExact(parts[1].Replace(',', '.'), @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out parsedEnd);
                _startTime = parsedStart;
                if (parsedEnd < parsedStart)
                {
                    _duration = TimeSpan.FromSeconds(1);
                }
                else
                {
                    _duration = parsedEnd - parsedStart;
                }
            }
            else
            {
                _startTime = TimeSpan.Zero;
                _duration = TimeSpan.FromSeconds(1);
            }
            UpdateTimeCodeProperty();
        }

        private void UpdateTimeCodeProperty()
        {
            TimeCode = $"{StartTime:hh\\:mm\\:ss\\,fff} --> {EndTime:hh\\:mm\\:ss\\,fff}";
        }

        public bool IsVoiced
        {
            get => _isVoiced;
            set
            {
                if (_isVoiced != value)
                {
                    _isVoiced = value;
                    OnPropertyChanged(nameof(IsVoiced));
                }
            }
        }

        public string VoicedAudioPath
        {
            get => _voicedAudioPath;
            set
            {
                if (_voicedAudioPath != value)
                {
                    _voicedAudioPath = value;
                    OnPropertyChanged(nameof(VoicedAudioPath));
                }
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public class CapCutProject : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Path { get; set; }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public enum AssetType
    {
        Video,
        Audio,
        Image,
        Subtitle,
        Blur

    }

    public class MediaAsset : INotifyPropertyChanged
    {
        public string ThumbnailBase64 { get; set; }
        private double _scaleX = 1.0;
        private double _scaleY = 1.0;
        private bool _isUniformScale = true;
        private double _rotation = 0.0;
        public double Rotation
        {
            get => _rotation;
            set
            {
                if (Math.Abs(_rotation - value) > 0.01)
                {
                    _rotation = value % 360.0;
                    OnPropertyChanged();
                }
            }
        }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Scale
        {
            get => _scaleX;
            set
            {
                if (_scaleX != value)
                {
                    _scaleX = value;
                    if (_isUniformScale)
                    {
                        _scaleY = value;
                        OnPropertyChanged(nameof(ScaleY));
                    }
                    OnPropertyChanged();
                }
            }
        }
        public double ScaleX
        {
            get => _scaleX;
            set
            {
                if (_scaleX != value)
                {
                    _scaleX = value;
                    if (_isUniformScale)
                    {
                        _scaleY = value;
                        OnPropertyChanged(nameof(ScaleY));
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Scale));
                }
            }
        }

        #region Blur Region Properties
        /// <summary>
        /// Cường độ mờ, chỉ sử dụng khi Type là Blur.
        /// </summary>
        public double BlurIntensity { get; set; }

        /// <summary>
        /// Đường dẫn đến file ảnh PNG mờ đã được tạo, chỉ sử dụng khi Type là Blur.
        /// </summary>
        public string GeneratedImagePath { get; set; }
        #endregion
        public double ScaleY
        {
            get => _scaleY;
            set
            {
                if (_scaleY != value)
                {
                    _scaleY = value;
                    if (_isUniformScale)
                    {
                        _scaleX = value;
                        OnPropertyChanged(nameof(ScaleX));
                        OnPropertyChanged(nameof(Scale));
                    }
                    OnPropertyChanged();
                }
            }
        }
        public bool IsUniformScale
        {
            get => _isUniformScale;
            set
            {
                if (_isUniformScale != value)
                {
                    _isUniformScale = value;
                    if (_isUniformScale)
                    {
                        ScaleY = ScaleX;
                    }
                    OnPropertyChanged();
                }
            }
        }
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public subphimv1.Waveform.WavePeakPyramid WavePeaks 
        { 
            get => _wavePeaks; 
            set 
            { 
                _wavePeaks = value; 
                OnPropertyChanged();
            } 
        }
        private subphimv1.Waveform.WavePeakPyramid _wavePeaks;

        // ========== [FIX] THÊM WAVEFORM AUTO-RELOAD ==========
        /// <summary>
        /// Tự động load WavePeaks từ cache nếu chưa có và file audio tồn tại
        /// </summary>
        public async System.Threading.Tasks.Task<subphimv1.Waveform.WavePeakPyramid> GetOrLoadWavePeaksAsync()
        {
            // Nếu đã có WavePeaks thì return ngay
            if (_wavePeaks != null) return _wavePeaks;
            
            // Nếu không phải audio file thì return null
            if (Type != AssetType.Audio || string.IsNullOrEmpty(FilePath) || !System.IO.File.Exists(FilePath))
                return null;

            try
            {
                // Thử load từ cache trước
                if (subphimv1.Waveform.WavePeakCache.TryLoad(FilePath, out var cachedPyramid))
                {
                    _wavePeaks = cachedPyramid;
                    OnPropertyChanged(nameof(WavePeaks));
                    return _wavePeaks;
                }

                // Nếu không có cache, build mới (async)
                var newPyramid = await subphimv1.Waveform.WavePeakCache.BuildAsync(FilePath, System.Threading.CancellationToken.None);
                _wavePeaks = newPyramid;
                OnPropertyChanged(nameof(WavePeaks));
                return _wavePeaks;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaAsset] Failed to load WavePeaks for {FilePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Đánh dấu rằng waveform cần được refresh (gọi khi có thay đổi audio properties)
        /// </summary>
        public void InvalidateWaveform()
        {
            _wavePeaks = null;
            OnPropertyChanged(nameof(WavePeaks));
        }
        // ========== END FIX ==========

        private double _positionX = 0.5;
        public double PositionX
        {
            get => _positionX;
            set { if (_positionX != value) { _positionX = value; OnPropertyChanged(); } }
        }
        private double _positionY = 0.5;
        public double PositionY
        {
            get => _positionY;
            set { if (_positionY != value) { _positionY = value; OnPropertyChanged(); } }
        }
        public string FilePath { get; set; }
        public string FileName => Path.GetFileName(FilePath);
        public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FilePath);
        public string FileExtension => Path.GetExtension(FilePath);
        public AssetType Type { get; set; }

        private System.Windows.Media.Imaging.BitmapImage _thumbnailSource;
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Windows.Media.Imaging.BitmapImage ThumbnailSource
        {
            get
            {
                if (_thumbnailSource == null && !string.IsNullOrEmpty(ThumbnailBase64))
                {
                    try
                    {
                        byte[] binaryData = System.Convert.FromBase64String(ThumbnailBase64);
                        var bi = new System.Windows.Media.Imaging.BitmapImage();
                        bi.BeginInit();
                        bi.StreamSource = new MemoryStream(binaryData);
                        bi.EndInit();
                        bi.Freeze();
                        _thumbnailSource = bi;
                    }
                    catch (Exception ex)
                    {
                    }
                }
                return _thumbnailSource;
            }
            set
            {
                if (_thumbnailSource != value)
                {
                    _thumbnailSource = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<float> WaveformData { get; set; }
        public TimeSpan Duration { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        private TimeSpan _startTime;
        public TimeSpan StartTime
        {
            get => _startTime;
            set
            {
                if (_startTime != value)
                {
                    _startTime = value;
                    OnPropertyChanged();
                }
            }
        }
        private double _volumeDb = 0.0;
        public double VolumeDb
        {
            get => _volumeDb;
            set
            {
                if (_volumeDb != value)
                {
                    // Dòng debug được thêm vào
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] MediaAsset.VolumeDb Setter: Asset '{FileName}' volume changed from {_volumeDb:F2} to {value:F2}.");
                    _volumeDb = value;
                    OnPropertyChanged();
                }
            }
        }

        private TimeSpan _trimStartOffset = TimeSpan.Zero;
        public TimeSpan TrimStartOffset
        {
            get => _trimStartOffset;
            set
            {
                if (_trimStartOffset != value)
                {
                    _trimStartOffset = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EffectiveDuration));
                }
            }
        }

        private TimeSpan _trimEndOffset = TimeSpan.Zero;
        public TimeSpan TrimEndOffset
        {
            get => _trimEndOffset;
            set
            {
                if (_trimEndOffset != value)
                {
                    _trimEndOffset = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EffectiveDuration));
                }
            }
        }
        private double _speed = 1.0;
        public double Speed
        {
            get => _speed;
            set
            {
                if (_speed != value && value > 0)
                {
                    _speed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EffectiveDuration));
                }
            }
        }

        // ========== ADVANCED AUDIO EFFECTS ==========
        private float _pitchSemitones = 0f;
        public float PitchSemitones
        {
            get => _pitchSemitones;
            set { if (Math.Abs(_pitchSemitones - value) > 0.01f) { _pitchSemitones = value; OnPropertyChanged(nameof(PitchSemitones)); } }
        }

        private float _formantShift = 0f;
        public float FormantShift
        {
            get => _formantShift;
            set { if (Math.Abs(_formantShift - value) > 0.01f) { _formantShift = value; OnPropertyChanged(nameof(FormantShift)); } }
        }

        private float _reverbMix = 0f;
        public float ReverbMix
        {
            get => _reverbMix;
            set { if (Math.Abs(_reverbMix - value) > 0.01f) { _reverbMix = value; OnPropertyChanged(nameof(ReverbMix)); } }
        }

        private int _echoDelayMs = 0;
        public int EchoDelayMs
        {
            get => _echoDelayMs;
            set { if (_echoDelayMs != value) { _echoDelayMs = value; OnPropertyChanged(nameof(EchoDelayMs)); } }
        }

        private float _distortionAmount = 0f;
        public float DistortionAmount
        {
            get => _distortionAmount;
            set { if (Math.Abs(_distortionAmount - value) > 0.01f) { _distortionAmount = value; OnPropertyChanged(nameof(DistortionAmount)); } }
        }

        // Filter settings
        private float _lowCutFreq = 0f;
        public float LowCutFreq
        {
            get => _lowCutFreq;
            set { if (Math.Abs(_lowCutFreq - value) > 0.1f) { _lowCutFreq = value; OnPropertyChanged(nameof(LowCutFreq)); } }
        }

        private float _highCutFreq = 20000f;
        public float HighCutFreq
        {
            get => _highCutFreq;
            set { if (Math.Abs(_highCutFreq - value) > 0.1f) { _highCutFreq = value; OnPropertyChanged(nameof(HighCutFreq)); } }
        }

        private float _midBoostGain = 0f;
        public float MidBoostGain
        {
            get => _midBoostGain;
            set { if (Math.Abs(_midBoostGain - value) > 0.1f) { _midBoostGain = value; OnPropertyChanged(nameof(MidBoostGain)); } }
        }

        // Robot effect
        private bool _isRobot = false;
        public bool IsRobot
        {
            get => _isRobot;
            set { if (_isRobot != value) { _isRobot = value; OnPropertyChanged(nameof(IsRobot)); } }
        }

        private float _robotFreq = 50f;
        public float RobotFreq
        {
            get => _robotFreq;
            set { if (Math.Abs(_robotFreq - value) > 0.1f) { _robotFreq = value; OnPropertyChanged(nameof(RobotFreq)); } }
        }

        // Tremolo effect
        private bool _isTremolo = false;
        public bool IsTremolo
        {
            get => _isTremolo;
            set { if (_isTremolo != value) { _isTremolo = value; OnPropertyChanged(nameof(IsTremolo)); } }
        }

        private float _tremoloFreq = 5f;
        public float TremoloFreq
        {
            get => _tremoloFreq;
            set { if (Math.Abs(_tremoloFreq - value) > 0.1f) { _tremoloFreq = value; OnPropertyChanged(nameof(TremoloFreq)); } }
        }

        private float _tremoloDepth = 0.5f;
        public float TremoloDepth
        {
            get => _tremoloDepth;
            set { if (Math.Abs(_tremoloDepth - value) > 0.01f) { _tremoloDepth = value; OnPropertyChanged(nameof(TremoloDepth)); } }
        }
        // ========== END ADVANCED AUDIO EFFECTS ==========

        [JsonIgnore]
        public TimeSpan EffectiveDuration
        {
            get
            {
                if (Speed <= 0) return TimeSpan.Zero;
                var trimmedDuration = Duration - TrimStartOffset - TrimEndOffset;
                var effectiveSeconds = Math.Max(0, trimmedDuration.TotalSeconds) / Speed;
                return TimeSpan.FromSeconds(effectiveSeconds);
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public MediaAsset Clone()
        {
            return (MediaAsset)this.MemberwiseClone();
        }
    }

    public class TimelineAudioClip : INotifyPropertyChanged
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public subphimv1.Waveform.WavePeakPyramid WavePeaks 
        { 
            get => _wavePeaks; 
            set 
            { 
                _wavePeaks = value; 
                OnPropertyChanged();
            } 
        }
        private subphimv1.Waveform.WavePeakPyramid _wavePeaks;

        // ========== [FIX] THÊM WAVEFORM AUTO-RELOAD CHO TIMELINEAUDIOCLIP ==========
        /// <summary>
        /// Tự động load WavePeaks từ cache nếu chưa có và file audio tồn tại
        /// </summary>
        public async System.Threading.Tasks.Task<subphimv1.Waveform.WavePeakPyramid> GetOrLoadWavePeaksAsync()
        {
            // Nếu đã có WavePeaks thì return ngay
            if (_wavePeaks != null) return _wavePeaks;
            
            // Nếu không có FilePath hoặc file không tồn tại
            if (string.IsNullOrEmpty(FilePath) || !System.IO.File.Exists(FilePath))
                return null;

            try
            {
                // Thử load từ cache trước
                if (subphimv1.Waveform.WavePeakCache.TryLoad(FilePath, out var cachedPyramid))
                {
                    _wavePeaks = cachedPyramid;
                    OnPropertyChanged(nameof(WavePeaks));
                    return _wavePeaks;
                }

                // Nếu không có cache, build mới (async)
                var newPyramid = await subphimv1.Waveform.WavePeakCache.BuildAsync(FilePath, System.Threading.CancellationToken.None);
                _wavePeaks = newPyramid;
                OnPropertyChanged(nameof(WavePeaks));
                return _wavePeaks;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TimelineAudioClip] Failed to load WavePeaks for {FilePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Đánh dấu rằng waveform cần được refresh
        /// </summary>
        public void InvalidateWaveform()
        {
            _wavePeaks = null;
            OnPropertyChanged(nameof(WavePeaks));
        }
        // ========== END FIX ==========

        private string _filePath;
        public string FilePath
        {
            get => _filePath;
            set { if (_filePath != value) { _filePath = value; OnPropertyChanged(nameof(FilePath)); } }
        }

        private TimeSpan _startTime;
        public TimeSpan StartTime
        {
            get => _startTime;
            set { if (_startTime != value) { _startTime = value; OnPropertyChanged(nameof(StartTime)); OnPropertyChanged(nameof(EndTime)); } }
        }

        // ========== [FIX] THÊM TRACKINDEX CHO AUDIO CLIP ==========
        private int _trackIndex = 1; // Mặc định = 1 (AUDIO_TRACK_START)
        /// <summary>
        /// Track index cho audio clip. Mặc định = 1 (Audio Track 1).
        /// Giá trị này được serialize để giữ nguyên vị trí khi Undo/Redo.
        /// </summary>
        public int TrackIndex
        {
            get => _trackIndex;
            set
            {
                if (_trackIndex != value)
                {
                    _trackIndex = value;
                    OnPropertyChanged(nameof(TrackIndex));
                }
            }
        }
        // ========== END FIX ==========

        private TimeSpan _originalDuration;
        public TimeSpan OriginalDuration
        {
            get => _originalDuration;
            set
            {
                if (_originalDuration != value)
                {
                    _originalDuration = value;
                    OnPropertyChanged(nameof(OriginalDuration));
                    OnPropertyChanged(nameof(EffectiveDuration));
                    OnPropertyChanged(nameof(EndTime));
                }
            }
        }

        private TimeSpan _trimStartOffset = TimeSpan.Zero;
        public TimeSpan TrimStartOffset
        {
            get => _trimStartOffset;
            set
            {
                var clampedValue = TimeSpan.FromMilliseconds(Math.Max(0, value.TotalMilliseconds));
                if (_trimStartOffset != clampedValue)
                {
                    _trimStartOffset = clampedValue;
                    OnPropertyChanged(nameof(TrimStartOffset));
                    OnPropertyChanged(nameof(EffectiveDuration));
                    OnPropertyChanged(nameof(EndTime));
                }
            }
        }
        private TimeSpan _trimEndOffset = TimeSpan.Zero;

        public TimeSpan TrimEndOffset
        {
            get => _trimEndOffset;
            set
            {
                var clampedValue = TimeSpan.FromMilliseconds(Math.Max(0, value.TotalMilliseconds));
                if (_trimEndOffset != clampedValue)
                {
                    _trimEndOffset = clampedValue;
                    OnPropertyChanged(nameof(TrimEndOffset));
                    OnPropertyChanged(nameof(EffectiveDuration));
                    OnPropertyChanged(nameof(EndTime));
                }
            }
        }
        [JsonIgnore]
        public TimeSpan EffectiveDuration
        {
            get
            {
                if (Speed <= 0) return TimeSpan.Zero;
                var trimmedDuration = OriginalDuration - TrimStartOffset - TrimEndOffset;
                var effectiveSeconds = Math.Max(0, trimmedDuration.TotalSeconds) / Speed;
                return TimeSpan.FromSeconds(effectiveSeconds);
            }
        }

        [JsonIgnore]
        public TimeSpan EndTime => StartTime + EffectiveDuration;

        private double _speed = 1.0;
        public double Speed
        {
            get => _speed;
            set
            {
                if (_speed != value && value > 0)
                {
                    _speed = value;
                    OnPropertyChanged(nameof(Speed));
                    OnPropertyChanged(nameof(EffectiveDuration));
                    OnPropertyChanged(nameof(EndTime));
                }
            }
        }

        private double _volumeDb = 0.0;
        public double VolumeDb
        {
            get => _volumeDb;
            set
            {
                if (_volumeDb != value)
                {
                    // Dòng debug được thêm vào
                    _volumeDb = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _fadeInDuration = 0.0;
        public double FadeInDuration
        {
            get => _fadeInDuration;
            set { if (_fadeInDuration != value) { _fadeInDuration = value; OnPropertyChanged(nameof(FadeInDuration)); } }
        }

        private double _fadeOutDuration = 0.0;
        public double FadeOutDuration
        {
            get => _fadeOutDuration;
            set { if (_fadeOutDuration != value) { _fadeOutDuration = value; OnPropertyChanged(nameof(FadeOutDuration)); } }
        }

        private bool _pitchCorrection = true;
        public bool PitchCorrection
        {
            get => _pitchCorrection;
            set { if (_pitchCorrection != value) { _pitchCorrection = value; OnPropertyChanged(nameof(PitchCorrection)); } }
        }
        public bool IsTts { get; set; } = false;

        /// <summary>Snapshot text lúc tạo TTS để xuất phụ đề khi user đã xóa phụ đề.</summary>
        public string CaptionTextSnapshot { get; set; } = null;

        /// <summary>Snapshot index phụ đề gốc (nếu biết).</summary>
        public int? SourceSubtitleIndexSnapshot { get; set; } = null;

        /// <summary>Kết quả detect im lặng nếu có bước tiền xử lý; không bắt buộc.</summary>
        public double? DetectedLeadSilenceMs { get; set; } = null;

        /// <summary>Kết quả detect im lặng nếu có bước tiền xử lý; không bắt buộc.</summary>
        public double? DetectedTailSilenceMs { get; set; } = null;

        // ========== ADVANCED AUDIO EFFECTS ==========
        private float _pitchSemitones = 0f;
        public float PitchSemitones
        {
            get => _pitchSemitones;
            set { if (Math.Abs(_pitchSemitones - value) > 0.01f) { _pitchSemitones = value; OnPropertyChanged(nameof(PitchSemitones)); } }
        }

        private float _formantShift = 0f;
        public float FormantShift
        {
            get => _formantShift;
            set { if (Math.Abs(_formantShift - value) > 0.01f) { _formantShift = value; OnPropertyChanged(nameof(FormantShift)); } }
        }

        private float _reverbMix = 0f;
        public float ReverbMix
        {
            get => _reverbMix;
            set { if (Math.Abs(_reverbMix - value) > 0.01f) { _reverbMix = value; OnPropertyChanged(nameof(ReverbMix)); } }
        }

        private int _echoDelayMs = 0;
        public int EchoDelayMs
        {
            get => _echoDelayMs;
            set { if (_echoDelayMs != value) { _echoDelayMs = value; OnPropertyChanged(nameof(EchoDelayMs)); } }
        }

        private float _distortionAmount = 0f;
        public float DistortionAmount
        {
            get => _distortionAmount;
            set { if (Math.Abs(_distortionAmount - value) > 0.01f) { _distortionAmount = value; OnPropertyChanged(nameof(DistortionAmount)); } }
        }

        // Filter settings
        private float _lowCutFreq = 0f;
        public float LowCutFreq
        {
            get => _lowCutFreq;
            set { if (Math.Abs(_lowCutFreq - value) > 0.1f) { _lowCutFreq = value; OnPropertyChanged(nameof(LowCutFreq)); } }
        }

        private float _highCutFreq = 20000f;
        public float HighCutFreq
        {
            get => _highCutFreq;
            set { if (Math.Abs(_highCutFreq - value) > 0.1f) { _highCutFreq = value; OnPropertyChanged(nameof(HighCutFreq)); } }
        }

        private float _midBoostGain = 0f;
        public float MidBoostGain
        {
            get => _midBoostGain;
            set { if (Math.Abs(_midBoostGain - value) > 0.1f) { _midBoostGain = value; OnPropertyChanged(nameof(MidBoostGain)); } }
        }

        // Robot effect
        private bool _isRobot = false;
        public bool IsRobot
        {
            get => _isRobot;
            set { if (_isRobot != value) { _isRobot = value; OnPropertyChanged(nameof(IsRobot)); } }
        }

        private float _robotFreq = 50f;
        public float RobotFreq
        {
            get => _robotFreq;
            set { if (Math.Abs(_robotFreq - value) > 0.1f) { _robotFreq = value; OnPropertyChanged(nameof(RobotFreq)); } }
        }

        // Tremolo effect
        private bool _isTremolo = false;
        public bool IsTremolo
        {
            get => _isTremolo;
            set { if (_isTremolo != value) { _isTremolo = value; OnPropertyChanged(nameof(IsTremolo)); } }
        }

        private float _tremoloFreq = 5f;
        public float TremoloFreq
        {
            get => _tremoloFreq;
            set { if (Math.Abs(_tremoloFreq - value) > 0.1f) { _tremoloFreq = value; OnPropertyChanged(nameof(TremoloFreq)); } }
        }

        private float _tremoloDepth = 0.5f;
        public float TremoloDepth
        {
            get => _tremoloDepth;
            set { if (Math.Abs(_tremoloDepth - value) > 0.01f) { _tremoloDepth = value; OnPropertyChanged(nameof(TremoloDepth)); } }
        }
        // ========== END ADVANCED AUDIO EFFECTS ==========

        public List<float> WaveformData { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CapCutTimeRange
    {
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
    }

    public class GeminiApiResponse
    {
        [JsonProperty("candidates")]
        public List<Candidate> Candidates { get; set; }

        [JsonProperty("promptFeedback")]
        public PromptFeedback PromptFeedback { get; set; }

        [JsonProperty("error")]
        public GeminiError Error { get; set; }
    }
    public class Candidate
    {
        [JsonProperty("content")]
        public Content Content { get; set; }

        [JsonProperty("finishReason")]
        public string FinishReason { get; set; }

        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("safetyRatings")]
        public List<SafetyRating> SafetyRatings { get; set; }
    }

    public class Content
    {
        [JsonProperty("parts")]
        public List<Part> Parts { get; set; }

        [JsonProperty("role")]
        public string Role { get; set; }
    }

    public class Part
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }

    public class SafetyRating
    {
        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("probability")]
        public string Probability { get; set; }
    }

    public class PromptFeedback
    {
        [JsonProperty("safetyRatings")]
        public List<SafetyRating> SafetyRatings { get; set; }
    }

    public class GeminiError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }
    public class TtsSmartCutEntry
    {
        [JsonProperty("index")]
        public int Index { get; set; }

        /// <summary>Đường dẫn tuyệt đối tới file audio TTS (hoặc đường dẫn tương đối nếu bạn muốn).</summary>
        [JsonProperty("audio_path")]
        public string AudioPath { get; set; }

        /// <summary>Start time (milliseconds, tính từ 0 của project timeline).</summary>
        [JsonProperty("start_ms")]
        public long StartMs { get; set; }

        /// <summary>End time (milliseconds). Với Dynamic mode có thể null, sẽ tính sau.</summary>
        [JsonProperty("end_ms")]
        public long? EndMs { get; set; }
    }

    public class TtsSmartCutManifest
    {
        [JsonProperty("project_name")]
        public string ProjectName { get; set; }

        /// <summary>Đường dẫn thư mục batch TTS, vd .../Projects/TTS/MyProject/2025-10-15_120101</summary>
        [JsonProperty("tts_batch_folder")]
        public string TtsBatchFolder { get; set; }

        /// <summary>Đường dẫn file SRT nguồn (nếu có).</summary>
        [JsonProperty("source_srt_path")]
        public string SourceSrtPath { get; set; }

        [JsonProperty("created_at_utc")]
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [JsonProperty("entries")]
        public List<TtsSmartCutEntry> Entries { get; set; } = new List<TtsSmartCutEntry>();
    }
}