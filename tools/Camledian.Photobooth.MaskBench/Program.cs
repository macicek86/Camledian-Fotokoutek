using Camledian.Photobooth.AI;
using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging;
using Camledian.Photobooth.Imaging.BackgroundSubtraction;
using Camledian.Photobooth.MaskBench;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// Measures background removal instead of eyeballing it. Every quality change to the keying pipeline
// should be run through this before and after, on the same scenes, because "looks better on my one
// test photo" has been wrong here more than once — the drift compensation that fixed a re-exposing
// camera also broke close-up subjects, and only a scored run showed it.
//
//   dotnet run --project tools/Camledian.Photobooth.MaskBench -- generate <sceneDir> [cutout.png] [coverage]
//   dotnet run --project tools/Camledian.Photobooth.MaskBench -- score <sceneDir> [model.onnx] [masksOutDir]
//
// generate builds a scene with known ground truth from a background photo in assets/. Pass a cut-out
// PNG of a real person on transparency for a realistic subject (see the README section below);
// without one a drawn stand-in is used. coverage is how much of the frame the subject fills — 0.3 is
// a guest at arm's length, 0.8 is one leaning into the lens, which is a genuinely different problem.
//
// score runs each available technique over every frame in the scene and prints the table. Give it a
// path to u2netp.onnx/u2net.onnx to include the AI and hybrid modes; without a model it scores the
// background-subtraction variants only.

if (args.Length < 2)
{
    Console.Error.WriteLine("Použití: generate <sceneDir> [cutout.png] [coverage] | score <sceneDir> [model.onnx] [masksOutDir]");
    return 1;
}

switch (args[0])
{
    case "generate":
    {
        var backgroundPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "backgrounds", "city-night.jpg");
        var coverage = args.Length > 3 && double.TryParse(args[3], out var parsed) ? parsed : 0.3;
        SceneGenerator.Generate(args[1], Path.GetFullPath(backgroundPath), args.Length > 2 ? args[2] : null, coverage);
        return 0;
    }

    case "score":
    {
        var scene = Scene.Load(args[1]);
        var modelPath = args.Length > 2 && File.Exists(args[2]) ? args[2] : null;
        var masksOut = args.Length > 3 ? args[3] : null;
        if (masksOut is not null)
        {
            Directory.CreateDirectory(masksOut);
        }

        var ai = modelPath is null
            ? null
            : new AiBackgroundRemovalProvider(
                () => new AiSettings { PreviewModelPath = modelPath, FinalModelPath = modelPath },
                NullLogger<AiBackgroundRemovalProvider>.Instance);

        Console.WriteLine($"{"snímek",-22}{"metoda",-16}{"IoU",7}{"ubráno",9}{"pozadí+",9}   rekvizity");
        foreach (var framePath in scene.FramePaths)
        {
            var frameName = Path.GetFileNameWithoutExtension(framePath);

            foreach (var (name, settings, halfResolution) in Variants(scene.Directory))
            {
                using var frame = Image.Load<Rgba32>(framePath);
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                var mask = BackgroundSubtractionProcessor.Apply(frame, scene.Reference, settings, halfResolution);
                elapsed.Stop();
                Report(frameName, name, mask, scene, elapsed.Elapsed.TotalMilliseconds, masksOut);
            }

            if (ai is not null)
            {
                using (var frame = Image.Load<Rgba32>(framePath))
                {
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    var mask = await ai.ApplyAsync(frame, BackgroundRemovalOptions.FinalRender);
                    elapsed.Stop();
                    Report(frameName, "ai", mask, scene, elapsed.Elapsed.TotalMilliseconds, masksOut);
                }

                using (var frame = Image.Load<Rgba32>(framePath))
                {
                    var settings = Defaults(scene.Directory);
                    var service = new BackgroundSubtractionRemovalService(() => settings);
                    var hybrid = new BackgroundSubtractionAiHybridProvider(service, ai);
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    var mask = await hybrid.ApplyAsync(frame, BackgroundRemovalOptions.FinalRender);
                    elapsed.Stop();
                    Report(frameName, "hybrid", mask, scene, elapsed.Elapsed.TotalMilliseconds, masksOut);
                }
            }
        }

        scene.Reference.Dispose();
        return 0;
    }

    default:
        Console.Error.WriteLine($"Neznámý příkaz '{args[0]}'.");
        return 1;
}

static BackgroundSubtractionSettings Defaults(string sceneDirectory) => new()
{
    ReferenceImagePath = Path.Combine(sceneDirectory, "reference.png"),
    ThresholdDistance = 40,
    FeatherPixels = 3,
    FillHolesPixels = 2,
    CompensateLightingDrift = true,
};

/// <summary>The variants worth comparing: current defaults, the preview path, and the behaviour from
/// before the drift compensation and hole filling existed, kept as the baseline every change is
/// measured against.</summary>
static IEnumerable<(string Name, BackgroundSubtractionSettings Settings, bool HalfResolution)> Variants(string sceneDirectory)
{
    var baseline = Defaults(sceneDirectory);
    baseline.CompensateLightingDrift = false;
    baseline.FillHolesPixels = 0;
    yield return ("odečítání-2025", baseline, false);

    yield return ("odečítání", Defaults(sceneDirectory), false);
    yield return ("odečítání-náhled", Defaults(sceneDirectory), true);
}

static void Report(string frameName, string method, float[] mask, Scene scene, double milliseconds, string? masksOut)
{
    var score = MaskScorer.Score(mask, scene);
    var props = string.Join(" ", score.PropRetention.Select(p => $"{p.Name}={p.Kept * 100:0}%"));
    Console.WriteLine(
        $"{frameName,-22}{method,-16}{score.Iou * 100,6:0.0}%{score.ForegroundLost * 100,8:0.0}%{score.BackgroundKept * 100,8:0.0}%   {props} ({milliseconds:0} ms)");

    if (masksOut is null)
    {
        return;
    }

    using var image = new Image<L8>(scene.Width, scene.Height);
    image.ProcessPixelRows(accessor =>
    {
        for (var y = 0; y < scene.Height; y++)
        {
            var row = accessor.GetRowSpan(y);
            for (var x = 0; x < scene.Width; x++)
            {
                row[x] = new L8((byte)Math.Clamp((int)Math.Round(mask[(y * scene.Width) + x] * 255f), 0, 255));
            }
        }
    });
    image.SaveAsPng(Path.Combine(masksOut, $"{frameName}-{method}.png"));
}
