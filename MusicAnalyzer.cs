using System;
using UnityEngine;
using UnityEngine.Audio;

namespace PartyTime
{
    internal sealed class MusicAnalyzer
    {
        private const int SpectrumSize = 1024;
        private const int BandCount = 14;
        private const float LowestFrequency = 44f;
        private const float HighestFrequency = 11000f;
        private const float BandFloor = 0.001f;

        private const double OnsetHistorySeconds = 1.5;
        private const int MinOnsetHistory = 10;
        private const float OnsetDeviations = 1.5f;
        private const float MinOnsetStrength = 0.1f;
        private const double MinBeatSeconds = 0.25;

        private const double TempoWindowSeconds = 8.0;
        private const int TempoRate = 100;
        private const int SpreadSlots = 3;
        private const float TempoIntervalSeconds = 0.5f;
        private const float MinBpm = 70f;
        private const float MaxBpm = 180f;
        private const float PreferredBpm = 120f;
        private const float MinTempoConfidence = 0.2f;
        private const float SameTempo = 0.04f;
        private const int LostTempoVotes = 4;
        private const float PhaseCaptureBeats = 0.25f;
        private const float PhaseCorrection = 0.25f;

        private const float PulseSeconds = 0.18f;
        private const float PeakDecaySeconds = 5f;
        private const float LevelSmoothingSeconds = 0.06f;
        private const int HistorySize = 2048;

        private readonly float[] _spectrum = new float[SpectrumSize];
        private readonly float[] _bands = new float[BandCount];
        private readonly float[] _previousBands = new float[BandCount];
        private readonly int[] _bandBins = new int[BandCount + 1];
        private readonly double[] _historyTimes = new double[HistorySize];
        private readonly float[] _historyStrengths = new float[HistorySize];
        private readonly float[] _envelope = new float[(int)(TempoWindowSeconds * TempoRate)];

        private AudioResource _song;
        private int _bandSampleRate;
        private int _historyHead;
        private int _historyCount;
        private double _lastSignature = double.NaN;
        private double _lastBlockTime = double.NaN;
        private double _lastOnsetTime = double.NegativeInfinity;
        private double _lastBeatTime = double.NegativeInfinity;
        private bool _hasPreviousBands;
        private float _tempoTimer;
        private float _phase;
        private float _candidateBpm;
        private int _candidateVotes;
        private int _missedTempoVotes;
        private float _loudnessPeak, _bassPeak, _treblePeak;
        private float _loudnessTarget, _bassTarget, _trebleTarget;

        public float Bpm { get; private set; }
        public float TempoConfidence { get; private set; }
        public bool TempoLocked { get; private set; }
        public bool Beat { get; private set; }
        public int BeatCount { get; private set; }
        public float Pulse { get; private set; }
        public float Loudness { get; private set; }
        public float Bass { get; private set; }
        public float Treble { get; private set; }

        public void Reset()
        {
            _song = null;
            _historyHead = 0;
            _historyCount = 0;
            _lastSignature = double.NaN;
            _lastBlockTime = double.NaN;
            _lastOnsetTime = double.NegativeInfinity;
            _lastBeatTime = double.NegativeInfinity;
            _hasPreviousBands = false;
            _tempoTimer = TempoIntervalSeconds;
            _phase = 0f;
            _candidateVotes = 0;
            _missedTempoVotes = 0;
            _loudnessPeak = _bassPeak = _treblePeak = 0f;
            Bpm = 0f;
            TempoConfidence = 0f;
            TempoLocked = false;
        }

        public void Update(AudioSource source, float deltaTime)
        {
            Beat = false;
            Pulse *= Mathf.Exp(-deltaTime / PulseSeconds);
            if (source != null && source.isPlaying)
            {
                if (source.resource != _song)
                {
                    Reset();
                    _song = source.resource;
                }
                source.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
                double signature = 0;
                for (int i = 0; i < SpectrumSize; i++) signature += _spectrum[i];
                if (signature != _lastSignature)
                {
                    _lastSignature = signature;
                    ProcessBlock(AudioSettings.dspTime, 1f / Mathf.Max(source.volume, 0.02f));
                }
                AdvanceClock(deltaTime);
                _tempoTimer -= deltaTime;
                if (_tempoTimer <= 0f)
                {
                    _tempoTimer = TempoIntervalSeconds;
                    EstimateTempo(AudioSettings.dspTime);
                }
            }
            else
            {
                _loudnessTarget = _bassTarget = _trebleTarget = 0f;
            }
            float follow = 1f - Mathf.Exp(-deltaTime / LevelSmoothingSeconds);
            Loudness = Mathf.Lerp(Loudness, _loudnessTarget, follow);
            Bass = Mathf.Lerp(Bass, _bassTarget, follow);
            Treble = Mathf.Lerp(Treble, _trebleTarget, follow);
        }

        private void ProcessBlock(double time, float gain)
        {
            UpdateBandBins();
            float strength = 0f;
            for (int b = 0; b < BandCount; b++)
            {
                float sum = 0f;
                for (int i = _bandBins[b]; i < _bandBins[b + 1]; i++) sum += _spectrum[i];
                sum *= gain;
                if (_hasPreviousBands) strength += Mathf.Max(0f, Mathf.Log(sum + BandFloor) - Mathf.Log(_previousBands[b] + BandFloor));
                _bands[b] = sum;
            }
            Array.Copy(_bands, _previousBands, BandCount);
            float blockSeconds = double.IsNaN(_lastBlockTime) ? 0f : (float)(time - _lastBlockTime);
            _lastBlockTime = time;

            float bass = _bands[0] + _bands[1] + _bands[2] + _bands[3];
            float treble = _bands[10] + _bands[11] + _bands[12] + _bands[13];
            float loudness = 0f;
            for (int b = 0; b < BandCount; b++) loudness += _bands[b];
            _bassTarget = Normalize(bass, ref _bassPeak, blockSeconds);
            _trebleTarget = Normalize(treble, ref _treblePeak, blockSeconds);
            _loudnessTarget = Normalize(loudness, ref _loudnessPeak, blockSeconds);

            if (!_hasPreviousBands)
            {
                _hasPreviousBands = true;
                return;
            }

            float mean = 0f, squares = 0f;
            int count = 0;
            for (int k = 0; k < _historyCount; k++)
            {
                int index = (_historyHead - 1 - k + HistorySize) % HistorySize;
                if (time - _historyTimes[index] > OnsetHistorySeconds) break;
                mean += _historyStrengths[index];
                squares += _historyStrengths[index] * _historyStrengths[index];
                count++;
            }
            bool onset = false;
            if (count >= MinOnsetHistory)
            {
                mean /= count;
                float deviation = Mathf.Sqrt(Mathf.Max(0f, squares / count - mean * mean));
                onset = strength > mean + OnsetDeviations * deviation && strength > MinOnsetStrength && time - _lastOnsetTime >= MinBeatSeconds;
            }
            _historyTimes[_historyHead] = time;
            _historyStrengths[_historyHead] = strength;
            _historyHead = (_historyHead + 1) % HistorySize;
            _historyCount = Math.Min(_historyCount + 1, HistorySize);

            if (!onset) return;
            _lastOnsetTime = time;
            if (!TempoLocked)
            {
                FireBeat(time);
                return;
            }
            float error = _phase < 0.5f ? _phase : _phase - 1f;
            if (Mathf.Abs(error) <= PhaseCaptureBeats) _phase = Mathf.Repeat(_phase - error * PhaseCorrection, 1f);
        }

        private void AdvanceClock(float deltaTime)
        {
            if (!TempoLocked) return;
            _phase += deltaTime * Bpm / 60f;
            if (_phase < 1f) return;
            _phase -= Mathf.Floor(_phase);
            FireBeat(AudioSettings.dspTime);
        }

        private void FireBeat(double time)
        {
            if (time - _lastBeatTime < MinBeatSeconds) return;
            _lastBeatTime = time;
            Beat = true;
            BeatCount++;
            Pulse = 1f;
        }

        private void EstimateTempo(double now)
        {
            if (_historyCount == 0) return;
            int oldest = (_historyHead - _historyCount + HistorySize) % HistorySize;
            if (now - _historyTimes[oldest] < TempoWindowSeconds * 0.5) return;

            int size = _envelope.Length;
            Array.Clear(_envelope, 0, size);
            double start = now - TempoWindowSeconds;
            for (int k = 0; k < _historyCount; k++)
            {
                int index = (_historyHead - 1 - k + HistorySize) % HistorySize;
                if (_historyTimes[index] < start) break;
                float position = (float)((_historyTimes[index] - start) * TempoRate);
                int center = Mathf.RoundToInt(position);
                for (int slot = Math.Max(0, center - SpreadSlots); slot <= Math.Min(size - 1, center + SpreadSlots); slot++)
                    _envelope[slot] += _historyStrengths[index] * Mathf.Max(0f, 1f - Mathf.Abs(slot - position) / (SpreadSlots + 0.5f));
            }
            float mean = 0f;
            for (int j = 0; j < size; j++) mean += _envelope[j];
            mean /= size;
            float energy = 0f;
            for (int j = 0; j < size; j++)
            {
                _envelope[j] -= mean;
                energy += _envelope[j] * _envelope[j];
            }
            if (energy <= 1e-6f)
            {
                MissTempo();
                return;
            }

            int shortest = Mathf.FloorToInt(TempoRate * 60f / MaxBpm), longest = Mathf.CeilToInt(TempoRate * 60f / MinBpm);
            int bestLag = 0;
            float bestScore = 0f;
            for (int lag = shortest; lag <= longest; lag++)
            {
                float score = TempoScore(lag);
                if (score <= bestScore) continue;
                bestScore = score;
                bestLag = lag;
            }
            if (bestLag == 0)
            {
                MissTempo();
                return;
            }
            float before = TempoScore(bestLag - 1), after = TempoScore(bestLag + 1);
            float curvature = before - 2f * bestScore + after;
            float offset = curvature < 0f ? Mathf.Clamp(0.5f * (before - after) / curvature, -0.5f, 0.5f) : 0f;
            float bestBpm = TempoRate * 60f / (bestLag + offset);
            TempoConfidence = bestScore / energy;
            if (TempoConfidence < MinTempoConfidence)
            {
                MissTempo();
                return;
            }
            _missedTempoVotes = 0;
            if (!TempoLocked)
            {
                LockTempo(bestBpm, now);
            }
            else if (Mathf.Abs(bestBpm - Bpm) <= Bpm * SameTempo)
            {
                Bpm = Mathf.Lerp(Bpm, bestBpm, 0.3f);
                _candidateVotes = 0;
            }
            else if (_candidateVotes > 0 && Mathf.Abs(bestBpm - _candidateBpm) <= _candidateBpm * SameTempo)
            {
                if (++_candidateVotes >= 2) LockTempo(bestBpm, now);
            }
            else
            {
                _candidateBpm = bestBpm;
                _candidateVotes = 1;
            }
        }

        private void LockTempo(float bpm, double now)
        {
            Bpm = bpm;
            TempoLocked = true;
            _candidateVotes = 0;
            _phase = double.IsInfinity(_lastOnsetTime) ? 0f : Mathf.Repeat((float)(now - _lastOnsetTime) * bpm / 60f, 1f);
        }

        private void MissTempo()
        {
            if (TempoLocked && ++_missedTempoVotes >= LostTempoVotes) TempoLocked = false;
        }

        private float TempoScore(int lag)
        {
            float score = Autocorrelation(lag) + 0.5f * Autocorrelation(lag * 2);
            float octaves = Mathf.Log(TempoRate * 60f / lag / PreferredBpm, 2f);
            return score * Mathf.Exp(-0.5f * octaves * octaves);
        }

        private float Autocorrelation(int lag)
        {
            int size = _envelope.Length;
            int count = size - lag;
            if (count <= 0) return 0f;
            float sum = 0f;
            for (int j = 0; j < count; j++) sum += _envelope[j] * _envelope[j + lag];
            return sum * size / count;
        }

        private void UpdateBandBins()
        {
            int sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate == _bandSampleRate) return;
            _bandSampleRate = sampleRate;
            float binHz = sampleRate * 0.5f / SpectrumSize;
            for (int b = 0; b <= BandCount; b++)
            {
                float frequency = LowestFrequency * Mathf.Pow(HighestFrequency / LowestFrequency, (float)b / BandCount);
                int bin = Mathf.Clamp(Mathf.RoundToInt(frequency / binHz), 1, SpectrumSize - BandCount + b);
                if (b > 0 && bin <= _bandBins[b - 1]) bin = _bandBins[b - 1] + 1;
                _bandBins[b] = bin;
            }
        }

        private static float Normalize(float value, ref float peak, float seconds)
        {
            peak = Mathf.Max(value, peak * Mathf.Exp(-seconds / PeakDecaySeconds));
            return peak > 1e-4f ? value / peak : 0f;
        }
    }
}
