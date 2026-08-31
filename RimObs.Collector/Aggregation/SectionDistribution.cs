using HdrHistogram;

namespace RimWorks.RimObs.Collector.Aggregation;

public sealed class SectionDistribution {
    public const int BucketCount = 300;

    private const long LowestDiscernible = 1;
    private const long HighestTrackable = 10_000_000_000;
    private const int SignificantDigits = 2;

    private readonly object _gate = new();
    private readonly LongHistogram _histogram;
    private readonly long[] _bucketEpochs = new long[BucketCount];
    private readonly long[] _bucketCounts = new long[BucketCount];
    private readonly long[] _bucketTicks = new long[BucketCount];

    public SectionDistribution() {
        _histogram = new LongHistogram(LowestDiscernible, HighestTrackable, SignificantDigits);
    }

    public void Record(long epochSeconds, long elapsedTicks) {
        long value = Math.Clamp(elapsedTicks, LowestDiscernible, HighestTrackable);
        int slot = (int)(((epochSeconds % BucketCount) + BucketCount) % BucketCount);
        lock (_gate) {
            _histogram.RecordValue(value);
            if (_bucketEpochs[slot] != epochSeconds) {
                _bucketEpochs[slot] = epochSeconds;
                _bucketCounts[slot] = 0;
                _bucketTicks[slot] = 0;
            }
            _bucketCounts[slot]++;
            _bucketTicks[slot] += elapsedTicks;
        }
    }

    public PercentileSnapshot SnapshotPercentiles() {
        lock (_gate) {
            if (_histogram.TotalCount == 0)
                return PercentileSnapshot.Empty;
            return new PercentileSnapshot(
                _histogram.GetValueAtPercentile(50),
                _histogram.GetValueAtPercentile(95),
                _histogram.GetValueAtPercentile(99)
            );
        }
    }

    public TimelineBucket[] SnapshotTimeline(long nowEpochSeconds) {
        long oldest = nowEpochSeconds - BucketCount + 1;
        List<TimelineBucket> points = new(BucketCount);
        lock (_gate) {
            for (int i = 0; i < BucketCount; i++) {
                long epoch = _bucketEpochs[i];
                if (epoch < oldest || epoch > nowEpochSeconds || _bucketCounts[i] == 0)
                    continue;
                points.Add(new TimelineBucket(epoch, _bucketCounts[i], _bucketTicks[i]));
            }
        }
        points.Sort(static (a, b) => a.EpochSeconds.CompareTo(b.EpochSeconds));
        return points.ToArray();
    }
}
