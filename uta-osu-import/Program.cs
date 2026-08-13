// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Game.Rulesets.Karaoke.Integration.Uta;

if (args.Length is < 1 or > 2 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: uta-osu-import <song.utz> [output.osz]");
    Console.WriteLine("Validates the UTZ package and preserves its audio, cover, video, lyrics and pitch chart.");
    return args.Length == 1 ? 0 : 2;
}

string input = Path.GetFullPath(args[0]);
if (!File.Exists(input))
{
    Console.Error.WriteLine($"Input file does not exist: {input}");
    return 2;
}

string output = args.Length == 2
    ? Path.GetFullPath(args[1])
    : Path.ChangeExtension(input, ".osz");

if (string.Equals(input, output, StringComparison.Ordinal))
{
    Console.Error.WriteLine("Input and output paths must be different.");
    return 2;
}

try
{
    string? directory = Path.GetDirectoryName(output);
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);

    UtzBeatmapSetConverter.Convert(input, output);
    Console.WriteLine(output);
    return 0;
}
catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
