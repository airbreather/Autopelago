using System.Collections;

public sealed class GameSimulation(
    int totalItemCount,
    int progressionItemCount,
    (int Threshold, int DC)[] stepwiseDifficultyThresholds,
    int maxDC,
    int failuresPerModifierIncrease)
{
    public long Simulate(int seed)
    {
        Random r = new(seed);
        BitArray isProgression = new(totalItemCount);
        for (int needed = progressionItemCount; needed > 0;)
        {
            int i = r.Next(totalItemCount);
            if (!isProgression[i])
            {
                isProgression[i] = true;
                --needed;
            }
        }

        long steps = 0;
        int foundProgressionItemCount = 0;
        int nextMod = 0;
        BitArray found = new(totalItemCount);
        while (foundProgressionItemCount < progressionItemCount)
        {
            ++steps;
            int currDC = maxDC;
            foreach ((int threshold, int DC) in stepwiseDifficultyThresholds)
            {
                if (foundProgressionItemCount < threshold && DC < currDC)
                {
                    currDC = DC;
                }
            }

            if (r.Next(1, 21) + (nextMod / failuresPerModifierIncrease) < currDC)
            {
                ++nextMod;
                continue;
            }

            nextMod = 0;
            int i;
            while (true)
            {
                i = r.Next(totalItemCount);
                if (!found[i])
                {
                    break;
                }
            }

            found[i] = true;
            if (isProgression[i])
            {
                ++foundProgressionItemCount;
            }
        }

        while (true)
        {
            ++steps;
            if (r.Next(1, 21) + (nextMod / failuresPerModifierIncrease) < maxDC)
            {
                ++nextMod;
                continue;
            }

            return steps;
        }
    }
}
