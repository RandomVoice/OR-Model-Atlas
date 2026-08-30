using System.Collections.Generic;

namespace CuttingStockLearning.Models
{
    public static class InitialPatternFactory
    {
        public static List<Pattern> Create(
            CuttingStockData data)
        {
            var patterns =
                new List<Pattern>();

            for (int i = 0; i < data.NumberOfItems; i++)
            {
                var fill =
                    new int[data.NumberOfItems];

                fill[i] = 1;

                patterns.Add(
                    new Pattern(
                        id: i,
                        cost: 1,
                        fill: fill));
            }

            return patterns;
        }
    }
}
