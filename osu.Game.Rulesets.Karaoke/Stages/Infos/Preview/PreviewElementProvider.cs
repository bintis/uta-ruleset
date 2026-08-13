// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;

namespace osu.Game.Rulesets.Karaoke.Stages.Infos.Preview;

public class PreviewElementProvider : StageElementProvider<PreviewStageInfo>
{
    public PreviewElementProvider(PreviewStageInfo stageInfo, bool displayNotePlayfield)
        : base(stageInfo, displayNotePlayfield)
    {
    }

    public override IEnumerable<IStageElement> GetElements()
    {
        // Song media belongs to lazer's background/video layer. The old in-stage
        // beatmap card duplicated that information and obscured the video.
        yield break;
    }
}
