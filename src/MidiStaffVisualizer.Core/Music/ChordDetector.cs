namespace MidiStaffVisualizer.Core.Music;

/// <summary>
/// 和弦识别结果。Root 是根音（大字号），Quality 是和弦性质（小字号）——和参考视频里
/// 「G + sus2(no5)」的排版一致。
/// </summary>
public readonly record struct ChordInfo(string Root, string Quality, string BassLabel, int NoteCount)
{
    public static readonly ChordInfo Empty = new(string.Empty, string.Empty, string.Empty, 0);

    public bool IsEmpty => NoteCount == 0;

    /// <summary>单音时只有一个音名，没有性质。</summary>
    public bool IsSingleNote => NoteCount > 0 && Quality.Length == 0 && Root.Length > 0;

    public string Display => IsEmpty
        ? string.Empty
        : string.IsNullOrEmpty(Quality)
            ? Root + BassLabel
            : $"{Root} {Quality}{BassLabel}";
}

/// <summary>
/// 和弦识别：在"所有音都属于模板、且最多只缺一个音（五音/三音）"的约束下给每个候选打分，
/// 取分最高的写法。缺五音时按参考视频的习惯写成 sus2(no5) 这种形式。
/// </summary>
public static class ChordDetector
{
    // Quality, 相对根音的半音集合, 罕见度（越大越不优先）
    private static readonly (string Quality, int[] Intervals, double Rank)[] Templates =
    [
        ("", [0, 4, 7], 0),
        ("m", [0, 3, 7], 0),
        ("7", [0, 4, 7, 10], 0),
        ("maj7", [0, 4, 7, 11], 0),
        ("m7", [0, 3, 7, 10], 0),
        ("sus4", [0, 5, 7], 1),
        ("sus2", [0, 2, 7], 1),
        ("6", [0, 4, 7, 9], 1),
        ("5", [0, 7], 2),
        ("add9", [0, 4, 7, 2], 2),
        ("9", [0, 4, 7, 10, 2], 1),
        ("maj9", [0, 4, 7, 11, 2], 2),
        ("m9", [0, 3, 7, 10, 2], 2),
        ("dim", [0, 3, 6], 3),
        ("aug", [0, 4, 8], 3),
        ("m6", [0, 3, 7, 9], 3),
        ("m7b5", [0, 3, 6, 10], 3),
        ("dim7", [0, 3, 6, 9], 4),
        ("7sus4", [0, 5, 7, 10], 4),
        ("madd9", [0, 3, 7, 2], 4),
        ("69", [0, 4, 7, 9, 2], 4),
        ("mMaj7", [0, 3, 7, 11], 5),
        ("7b5", [0, 4, 6, 10], 5),
        ("7#5", [0, 4, 8, 10], 5),
        ("11", [0, 4, 7, 10, 2, 5], 5),
        ("13", [0, 4, 7, 10, 2, 9], 6),
    ];

    /// <summary>可以缺失的音：7 = 五音，4 / 3 = 三音（7 和弦省略三音很常见）。</summary>
    private static bool CanOmit(int interval) => interval is 7 or 4 or 3;

    public static ChordInfo Detect(IReadOnlyList<int>? notes, bool preferFlats)
    {
        if (notes is null || notes.Count == 0)
        {
            return ChordInfo.Empty;
        }

        int[] ordered = notes.Where(n => n is >= 0 and <= 127).Distinct().OrderBy(n => n).ToArray();
        if (ordered.Length == 0)
        {
            return ChordInfo.Empty;
        }

        int bassNote = ordered[0];
        int bassPitchClass = Pitch.PitchClass(bassNote);
        int[] pitchClasses = ordered.Select(Pitch.PitchClass).Distinct().ToArray();

        if (pitchClasses.Length == 1)
        {
            return new ChordInfo(Pitch.StepName(bassNote, preferFlats), string.Empty, string.Empty, ordered.Length);
        }

        double bestScore = double.NegativeInfinity;
        string bestRoot = string.Empty;
        string bestQuality = string.Empty;
        int bestRootPitchClass = -1;

        foreach (int root in pitchClasses)
        {
            var intervals = pitchClasses.Select(pc => ((pc - root) % 12 + 12) % 12).ToHashSet();

            foreach (var (quality, template, rank) in Templates)
            {
                var set = template.ToHashSet();

                int matched = set.Count(i => intervals.Contains(i));
                int extra = intervals.Count(i => !set.Contains(i));
                int[] missing = set.Where(i => !intervals.Contains(i)).ToArray();

                if (extra > 0)
                {
                    continue;   // 有音落在模板之外，说明不是这个和弦
                }

                if (missing.Length > 1 || (missing.Length == 1 && !CanOmit(missing[0])))
                {
                    continue;
                }

                double score = matched * 10.0;
                score -= missing.Length * 3.5;
                score -= rank * 0.5;
                if (root == bassPitchClass)
                {
                    score += 2.0;
                }

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestRootPitchClass = root;
                bestRoot = Pitch.StepName(60 + root, preferFlats);
                bestQuality = quality + (missing.Length == 0
                    ? string.Empty
                    : missing[0] == 7 ? "(no5)" : "(no3)");
            }
        }

        if (bestRoot.Length == 0)
        {
            // 兜底：认不出来就直接列出音名
            string fallback = string.Join(" ", ordered.Select(n => Pitch.StepName(n, preferFlats)));
            return new ChordInfo(fallback, string.Empty, string.Empty, ordered.Length);
        }

        // 低音不是根音时写成 C/E 这样的转位写法
        string bassLabel = bassPitchClass == bestRootPitchClass
            ? string.Empty
            : "/" + Pitch.StepName(bassNote, preferFlats);

        return new ChordInfo(bestRoot, bestQuality, bassLabel, ordered.Length);
    }
}
