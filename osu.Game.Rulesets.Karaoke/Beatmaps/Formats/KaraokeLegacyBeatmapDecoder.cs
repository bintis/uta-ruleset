// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Rulesets.Karaoke.Edit.Generator.Lyrics.Notes;
using osu.Game.Rulesets.Karaoke.Integration.Formats;
using osu.Game.Rulesets.Karaoke.Integration.Uta;
using osu.Game.Rulesets.Karaoke.Objects;
using osu.Game.Rulesets.Karaoke.UI.Uta;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.Karaoke.Beatmaps.Formats;

public class KaraokeLegacyBeatmapDecoder : LegacyBeatmapDecoder
{
    public new const int LATEST_VERSION = 1;

    public new static void Register()
    {
        AddDecoder<Beatmap>("karaoke file format v", m => new KaraokeLegacyBeatmapDecoder(Parsing.ParseInt(m.Split('v').Last())));

        // use this weird way to let all the fall-back beatmap(include karaoke beatmap) become karaoke beatmap.
        SetFallbackDecoder<Beatmap>(() => new KaraokeLegacyBeatmapDecoder());
    }

    public KaraokeLegacyBeatmapDecoder(int version = LATEST_VERSION)
        : base(version)
    {
    }

    private readonly IList<string> karFormatLines = new List<string>();
    private readonly IList<string> noteLines = new List<string>();
    private readonly IList<string> translations = new List<string>();
    private readonly IList<string> utaLyricLines = new List<string>();
    private readonly IList<string> utaNoteLines = new List<string>();
    private string? utaConfigLine;
    private IReadOnlyList<UtaTranscriptSegment> normalizedUtaSegments = Array.Empty<UtaTranscriptSegment>();

    protected override void ParseLine(Beatmap beatmap, Section section, string line, bool isPrimaryStream)
    {
        if (section != Section.HitObjects)
        {
            // Mode 111 is the native karaoke marker. Every standard lazer ruleset mode
            // must continue through the base decoder; otherwise merely installing this
            // ruleset incorrectly reclassifies the user's whole library as karaoke.
            if (line.StartsWith("Mode", StringComparison.Ordinal))
            {
                string? mode = line.Split(':').ElementAtOrDefault(1)?.Trim();
                if (mode == "111")
                {
                    beatmap.BeatmapInfo.Ruleset = new KaraokeRuleset().RulesetInfo;
                    return;
                }
            }

            base.ParseLine(beatmap, section, line, isPrimaryStream);
            return;
        }

        if (line.StartsWith("@utaconfig=", StringComparison.OrdinalIgnoreCase))
        {
            utaConfigLine = line[(line.IndexOf('=') + 1)..];
        }
        else if (line.StartsWith("@utalyric=", StringComparison.OrdinalIgnoreCase))
        {
            utaLyricLines.Add(line[(line.IndexOf('=') + 1)..]);
        }
        else if (line.StartsWith("@utanote=", StringComparison.OrdinalIgnoreCase))
        {
            utaNoteLines.Add(line[(line.IndexOf('=') + 1)..]);
        }
        else if (line.ToLower().StartsWith("@ruby", StringComparison.Ordinal))
        {
            // kar format queue
            karFormatLines.Add(line);
        }
        else if (line.ToLower().StartsWith("@note", StringComparison.Ordinal))
        {
            // add tone line queue
            noteLines.Add(line);
        }
        else if (line.ToLower().StartsWith("@tr", StringComparison.Ordinal))
        {
            // add translation queue
            translations.Add(line);
        }
        else if (line.StartsWith('@'))
        {
            // Remove @ in time tag and add into kar queue
            karFormatLines.Add(line[1..]);
        }
        else if (line.ToLower() == "end")
        {
            if (utaLyricLines.Count > 0)
            {
                var segments = utaLyricLines.Select(decodeLine<UtaTranscriptSegment>).ToArray();
                normalizedUtaSegments = UtaLyricsTimeline.Normalize(segments);

                beatmap.HitObjects = segments.Select(createUtaLyric)
                                                    .OfType<HitObject>()
                                                    .ToList();
            }
            else
            {
                string content = string.Join("\n", karFormatLines);
                var decoder = new KarDecoder();
                beatmap.HitObjects = decoder.Decode(content).OfType<HitObject>().ToList();
            }

            if (utaNoteLines.Count > 0)
            {
                bool octaveTolerance = utaConfigLine != null && decodeLine<UtaBeatmapMetadata>(utaConfigLine).OctaveTolerance;
                processUtaNotes(beatmap, utaNoteLines, octaveTolerance);
            }
            else
                processNotes(beatmap, noteLines);

            processTranslations(beatmap, translations);
            processUtaConfig(beatmap, utaConfigLine, normalizedUtaSegments);
        }
    }

    private static void processUtaConfig(Beatmap beatmap, string? encoded, IReadOnlyList<UtaTranscriptSegment> segments)
    {
        if (encoded == null)
            return;

        var metadata = decodeLine<UtaBeatmapMetadata>(encoded);
        var properties = beatmap.HitObjects.OfType<LegacyProperties>().FirstOrDefault();
        if (properties == null)
        {
            properties = new LegacyProperties();
            beatmap.HitObjects.Add(properties);
        }

        properties.UtaPackageId = metadata.PackageId;
        properties.UtaOctaveTolerance = metadata.OctaveTolerance;
        properties.UtaGuideVocalsFile = metadata.GuideVocalsFile;
        properties.UtaOriginalAudioFile = metadata.OriginalAudioFile;
        properties.UtaTranscriptSegments = segments;
        properties.UtaCentreMidi = metadata.CentreMidi;
    }

    private static void processUtaNotes(Beatmap beatmap, IEnumerable<string> encodedNotes, bool octaveTolerance)
    {
        beatmap.HitObjects.RemoveAll(x => x is Note);
        var notes = encodedNotes.Select(decodeLine<UtaPitchNote>).ToArray();
        if (notes.Length == 0)
            return;

        int centreMidi = (int)Math.Round(notes.Select(note => note.Midi).Order().ElementAt(notes.Length / 2) / 12.0) * 12;

        foreach (var source in notes)
        {
            double relative = (source.Midi - centreMidi) / 2.0;
            int scale = (int)Math.Floor(relative);

            beatmap.HitObjects.Add(new Note
            {
                AuthoredStartTime = source.Start * 1000,
                AuthoredDuration = (source.End - source.Start) * 1000,
                Midi = source.Midi,
                NoteKind = source.Kind.ToString(),
                UtaOctaveTolerance = octaveTolerance,
                Tone = new Tone(scale, relative - scale >= 0.5),
                Display = true,
            });
        }
    }

    private static Lyric createUtaLyric(UtaTranscriptSegment segment)
    {
        var lyric = new Lyric
        {
            Text = segment.Text,
        };

        if (segment.Text.Length == 0)
            return lyric;

        int cursor = 0;
        double lastTime = segment.Start;

        foreach (var word in segment.Words)
        {
            int index = segment.Text.IndexOf(word.Word, cursor, StringComparison.Ordinal);
            if (index < 0 || word.Word.Length == 0)
                continue;

            int endIndex = Math.Min(segment.Text.Length - 1, index + word.Word.Length - 1);
            double start = Math.Clamp(word.Start, lastTime, segment.End);
            double end = Math.Clamp(word.End, start, segment.End);

            lyric.TimeTags.Add(new TimeTag(new TextIndex(index), start * 1000)
            {
                RomanisedSyllable = word.Reading,
                FirstSyllable = true,
            });
            lyric.TimeTags.Add(new TimeTag(new TextIndex(endIndex, TextIndex.IndexState.End), end * 1000));

            cursor = endIndex + 1;
            lastTime = end;
        }

        if (lyric.TimeTags.Count == 0)
        {
            lyric.TimeTags.Add(new TimeTag(new TextIndex(0), segment.Start * 1000));
            lyric.TimeTags.Add(new TimeTag(new TextIndex(segment.Text.Length - 1, TextIndex.IndexState.End), segment.End * 1000));
        }

        return lyric;
    }

    private static T decodeLine<T>(string encoded)
    {
        try
        {
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return JsonConvert.DeserializeObject<T>(json)
                   ?? throw new FormatException($"Empty UTZ beatmap payload for {typeof(T).Name}.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new FormatException($"Invalid UTZ beatmap payload for {typeof(T).Name}.", ex);
        }
    }

    private void processNotes(Beatmap beatmap, IList<string> lines)
    {
        var noteGenerator = new NoteGenerator(new NoteGeneratorConfig());

        // Remove all karaoke note
        beatmap.HitObjects.RemoveAll(x => x is Note);

        var lyrics = beatmap.HitObjects.OfType<Lyric>().ToList();

        for (int l = 0; l < lyrics.Count; l++)
        {
            var lyric = lyrics[l];
            string? line = lines.ElementAtOrDefault(l)?.Split('=').Last();

            // Create default note if not exist
            if (string.IsNullOrEmpty(line))
            {
                beatmap.HitObjects.AddRange(noteGenerator.Generate(lyric));
                continue;
            }

            string[] notes = line.Split(',');
            var defaultNotes = noteGenerator.Generate(lyric).ToList();
            int minNoteNumber = Math.Min(notes.Length, defaultNotes.Count);

            // Process each note
            for (int i = 0; i < minNoteNumber; i++)
            {
                string note = notes[i];
                var defaultNote = defaultNotes[i];

                // Support multi note in one time tag, format like ([1;0.5;か]|1#|...)
                if (!note.StartsWith('(') || !note.EndsWith(')'))
                {
                    // Process and add note
                    applyNote(defaultNote, note);
                    beatmap.HitObjects.Add(defaultNote);
                }
                else
                {
                    float startPercentage = 0;
                    string[] rubyNotes = note.Replace("(", string.Empty).Replace(")", string.Empty).Split('|');

                    for (int j = 0; j < rubyNotes.Length; j++)
                    {
                        string rubyNote = rubyNotes[j];

                        string tone;
                        float percentage = (float)Math.Round((float)1 / rubyNotes.Length, 2, MidpointRounding.AwayFromZero);
                        string? ruby = defaultNote.RubyText?.ElementAtOrDefault(j).ToString();

                        // Format like [1;0.5;か]
                        if (note.StartsWith('[') && note.EndsWith(']'))
                        {
                            string[] rubyNoteProperty = note.Replace("[", string.Empty).Replace("]", string.Empty).Split(';');

                            // Copy tome property
                            tone = rubyNoteProperty[0];

                            // Copy percentage property
                            if (rubyNoteProperty.Length >= 2)
                                float.TryParse(rubyNoteProperty[1], out percentage);

                            // Copy text property
                            if (rubyNoteProperty.Length >= 3)
                                ruby = rubyNoteProperty[2];
                        }
                        else
                        {
                            tone = rubyNote;
                        }

                        // Split note and apply them
                        var splitDefaultNote = SliceNote(defaultNote, startPercentage, percentage);
                        startPercentage += percentage;
                        if (!string.IsNullOrEmpty(ruby))
                            splitDefaultNote.Text = ruby;

                        // Process and add note
                        applyNote(splitDefaultNote, tone);
                        beatmap.HitObjects.Add(splitDefaultNote);
                    }
                }
            }
        }

        static void applyNote(Note note, string noteStr, string? ruby = null, double? duration = null)
        {
            if (noteStr == "-")
                note.Display = false;
            else
            {
                note.Display = true;
                note.Tone = convertTone(noteStr);
            }

            if (!string.IsNullOrEmpty(ruby))
                note.Text = ruby;

            if (duration != null)
                note.Duration = duration.Value;

            //Support format : 1  1.  1.5  1+  1#
            static Tone convertTone(string tone)
            {
                bool half = false;

                if (tone.Contains('.') || tone.Contains('#'))
                {
                    half = true;

                    // only get digit part
                    tone = tone.Split('.').FirstOrDefault()?.Split('#').FirstOrDefault() ?? string.Empty;
                }

                if (!int.TryParse(tone, out int scale))
                    throw new InvalidCastException($"{tone} does not support in {nameof(KaraokeLegacyBeatmapDecoder)}");

                return new Tone
                {
                    Scale = scale,
                    Half = half,
                };
            }
        }
    }

    private void processTranslations(Beatmap beatmap, IEnumerable<string> translationLines)
    {
        var availableTranslations = new List<CultureInfo>();

        var lyrics = beatmap.HitObjects.OfType<Lyric>().ToList();
        var translations = translationLines.Select(translation => new
        {
            key = translation.Split('=').FirstOrDefault()?.Split('[').LastOrDefault()?.Split(']').FirstOrDefault(),
            value = translation.Split('=').LastOrDefault() ?? string.Empty,
        }).GroupBy(x => x.key, y => y.value).ToList();

        foreach (var translation in translations)
        {
            // get culture and translation
            string? languageCode = translation.Key;
            if (string.IsNullOrEmpty(languageCode))
                continue;

            var cultureInfo = new CultureInfo(languageCode);
            var values = translation.ToList();

            int size = Math.Min(lyrics.Count, translation.Count());

            for (int j = 0; j < size; j++)
            {
                lyrics[j].Translations.Add(cultureInfo, values[j]);
            }

            availableTranslations.Add(cultureInfo);
        }

        var dictionary = beatmap.HitObjects.OfType<LegacyProperties>().FirstOrDefault();
        if (dictionary == null)
        {
            dictionary = new LegacyProperties();
            beatmap.HitObjects.Add(dictionary);
        }

        dictionary.AvailableTranslationLanguages = availableTranslations;
    }

    internal static Note SliceNote(Note note, double startPercentage, double durationPercentage)
    {
        if (startPercentage < 0 || startPercentage + durationPercentage > 1)
            throw new ArgumentOutOfRangeException($"{nameof(Note)} cannot assign split range of start from {startPercentage} and duration {durationPercentage}");

        double durationFromStartTime = note.Duration * startPercentage;
        double secondNoteDuration = note.Duration * (1 - startPercentage - durationPercentage);

        // todo: there's no need to create the new note.
        var newNote = note.DeepClone();
        newNote.StartTimeOffset = note.StartTimeOffset + durationFromStartTime;
        newNote.EndTimeOffset = note.EndTimeOffset - secondNoteDuration;

        return newNote;
    }
}
