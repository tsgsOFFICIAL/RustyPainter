using System;
using System.Collections.Generic;
using System.Linq;

namespace RustyPainter.Imaging;

public static class MedianCut
{
    private sealed class Bin
    {
        public long Count;
        public long SumR, SumG, SumB;

        public double R => (double)SumR / Count;
        public double G => (double)SumG / Count;
        public double B => (double)SumB / Count;
    }

    /// <summary>
    /// Chooses up to <paramref name="maxColors"/> colors that represent the painted pixels of the picture well:
    /// the colors are grouped into boxes, and the box holding the most pixels is split in two until there are enough.
    /// Flat areas keep their exact color. Fewer colors means fewer color changes while painting.
    /// </summary>
    public static List<Rgb> BuildPalette(PixelBuffer image, int maxColors)
    {
        maxColors = Math.Max(1, maxColors);

        // Group pixels by their color at 5 bits per channel, remembering the true average of each group.
        Dictionary<int, Bin> bins = [];
        for (int i = 0; i < image.Data.Length; i += 4)
        {
            if (image.Data[i + 3] < Quantizer.AlphaThreshold)
                continue;

            byte r = image.Data[i], g = image.Data[i + 1], b = image.Data[i + 2];
            int key = (r >> 3) << 10 | (g >> 3) << 5 | (b >> 3);

            if (!bins.TryGetValue(key, out Bin? bin))
                bins[key] = bin = new Bin();

            bin.Count++;
            bin.SumR += r;
            bin.SumG += g;
            bin.SumB += b;
        }

        if (bins.Count == 0)
            return [new Rgb(0, 0, 0)];

        List<List<Bin>> boxes = [bins.Values.ToList()];

        while (boxes.Count < maxColors)
        {
            // Split the box with the most pixels that still has more than one color in it.
            List<Bin>? target = boxes.Where(box => box.Count > 1).OrderByDescending(box => box.Sum(bin => bin.Count)).FirstOrDefault();
            if (target is null)
                break;

            double rangeR = target.Max(b => b.R) - target.Min(b => b.R);
            double rangeG = target.Max(b => b.G) - target.Min(b => b.G);
            double rangeB = target.Max(b => b.B) - target.Min(b => b.B);

            Func<Bin, double> channel = rangeR >= rangeG && rangeR >= rangeB ? bin => bin.R : rangeG >= rangeB ? bin => bin.G : bin => bin.B;
            List<Bin> sorted = target.OrderBy(channel).ToList();

            long half = sorted.Sum(bin => bin.Count) / 2, running = 0;
            int split = 0;
            while (split < sorted.Count - 1 && running + sorted[split].Count <= half)
                running += sorted[split++].Count;

            // Never leave one side empty.
            split = Math.Clamp(split, 1, sorted.Count - 1);

            boxes.Remove(target);
            boxes.Add(sorted.GetRange(0, split));
            boxes.Add(sorted.GetRange(split, sorted.Count - split));
        }

        return boxes
            .Select(box =>
            {
                long count = box.Sum(bin => bin.Count);
                return new Rgb(
                    (byte)Math.Round((double)box.Sum(bin => bin.SumR) / count),
                    (byte)Math.Round((double)box.Sum(bin => bin.SumG) / count),
                    (byte)Math.Round((double)box.Sum(bin => bin.SumB) / count));
            })
            .Distinct()
            .ToList();
    }
}
