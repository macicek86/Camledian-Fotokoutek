using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.MaskBench;

/// <summary>
/// One test scene on disk: the empty-room reference photo an operator would capture, one or more
/// frames taken "during the event", and the ground-truth alpha of everything that was added to them.
/// Optional rectangles mark props, so a run can report specifically whether the sign someone is
/// holding survived — that is the case a person-shaped model gets wrong and no average IoU reveals.
///
/// Layout:
///   reference.png      the empty scene
///   frame-*.jpg        frames to key, JPEG because that is what a webcam actually delivers
///   truth.png          white where background removal must keep the pixel
///   props.txt          optional, one "name x y width height" per line
/// </summary>
public sealed class Scene
{
    public required string Directory { get; init; }
    public required Image<Rgba32> Reference { get; init; }
    public required bool[] Truth { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required IReadOnlyList<(string Name, Rectangle Area)> Props { get; init; }
    public required IReadOnlyList<string> FramePaths { get; init; }

    public static Scene Load(string directory)
    {
        var reference = Image.Load<Rgba32>(Path.Combine(directory, "reference.png"));
        using var truthImage = Image.Load<L8>(Path.Combine(directory, "truth.png"));

        var truth = new bool[truthImage.Width * truthImage.Height];
        truthImage.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < truthImage.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < truthImage.Width; x++)
                {
                    truth[(y * truthImage.Width) + x] = row[x].PackedValue > 127;
                }
            }
        });

        var propsPath = Path.Combine(directory, "props.txt");
        var props = new List<(string, Rectangle)>();
        if (File.Exists(propsPath))
        {
            foreach (var line in File.ReadAllLines(propsPath))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 5 && int.TryParse(parts[1], out var x) && int.TryParse(parts[2], out var y)
                    && int.TryParse(parts[3], out var w) && int.TryParse(parts[4], out var h))
                {
                    props.Add((parts[0], new Rectangle(x, y, w, h)));
                }
            }
        }

        return new Scene
        {
            Directory = directory,
            Reference = reference,
            Truth = truth,
            Width = truthImage.Width,
            Height = truthImage.Height,
            Props = props,
            FramePaths = [.. System.IO.Directory.GetFiles(directory, "frame-*.jpg").OrderBy(p => p)],
        };
    }
}
