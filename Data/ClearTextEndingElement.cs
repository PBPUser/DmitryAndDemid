using DmitryAndDemid.Screens;

namespace DmitryAndDemid.Data;

public class ClearTextEndingElement : EndingElement
{
    public override void Apply(EndingScreen screen)
    {
        screen.ClearText();
    }
}