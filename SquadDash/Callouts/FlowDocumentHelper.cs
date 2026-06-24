using System;
using System.Windows;
using System.Windows.Documents;

namespace SquadDash;
public static class FlowDocumentHelper {
    public static double GetLowestBlock(FlowDocument flowDocument) {
        if (flowDocument == null)
            return 0;


        double lowestBlockSoFar = double.MinValue;
        double highestBlockSoFar = double.MaxValue;
        foreach (var b in flowDocument.Blocks) {
            Rect endCharacterRect = b.ElementEnd.GetCharacterRect(LogicalDirection.Forward);

            Rect startCharacterRect = b.ElementStart.GetCharacterRect(LogicalDirection.Backward);

            if (double.IsInfinity(endCharacterRect.Width) || double.IsInfinity(endCharacterRect.Height))
                continue;

            if (endCharacterRect.Bottom > lowestBlockSoFar)
                lowestBlockSoFar = endCharacterRect.Bottom;
            if (startCharacterRect.Top < highestBlockSoFar)
                highestBlockSoFar = startCharacterRect.Top;
        }

        if (highestBlockSoFar < 0)
            return lowestBlockSoFar - highestBlockSoFar;

        return lowestBlockSoFar;
    }
}