using System;
using System.Collections.Generic;

using CuttingStockLearning.Models;
using CuttingStockLearning.Master;
using CuttingStockLearning.Pricing;

namespace CuttingStockLearning.ColumnGeneration
{
    public class ColumnGenerationSolver
    {
        public void Run()
        {
            Console.WriteLine("=========================================");
            Console.WriteLine("  Cutting Stock Column Generation");
            Console.WriteLine("=========================================");
            Console.WriteLine();

            //
            // STEP 1
            //
            Console.WriteLine("STEP 1 - Load Data");
            Console.WriteLine("------------------");

            CuttingStockData data = SampleData.Create();

            Console.WriteLine($"Roll Width = {data.RollWidth}");

            Console.WriteLine($"Number Of Items = {data.NumberOfItems}");

            Console.WriteLine();

            for (int i = 0; i < data.NumberOfItems; i++)
            {
                Console.WriteLine(
                    $"Item {i}: Width={data.ItemSizes[i]}, Demand={data.Demands[i]}");
            }

            //
            // STEP 2
            //
            Console.WriteLine();
            Console.WriteLine("STEP 2 - Create Initial Patterns");
            Console.WriteLine("-------------------------------");

            List<Pattern> patterns = InitialPatternFactory.Create(data);

            PrintPatterns(patterns);

            //
            // STEP 3
            //
            Console.WriteLine();
            Console.WriteLine("STEP 3 - Column Generation");
            Console.WriteLine("--------------------------");

            var masterProblem = new MasterProblem();

            var pricingProblem = new PricingProblem();

            int nextPatternId = patterns.Count;

            int iteration = 1;

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("=====================================");
                Console.WriteLine($"Iteration {iteration}");
                Console.WriteLine("=====================================");

                PrintPatterns(patterns);

                PrintMasterModel(data, patterns);

                //
                // Solve Master (LP Relaxation)
                //
                Console.WriteLine();
                Console.WriteLine("Solve Master (LP Relaxation)");
                Console.WriteLine("----------------------------");

                MasterResult masterResult = masterProblem.Solve(
                    data,
                    patterns);

                Console.WriteLine($"Optimal   = {masterResult.IsOptimal}");

                Console.WriteLine($"Objective = {masterResult.ObjectiveValue:F3}");

                PrintMasterSolution(
                    data,
                    patterns,
                    masterResult);

                //
                // Solve Pricing
                //
                Console.WriteLine();
                Console.WriteLine("Solve Pricing");
                Console.WriteLine("-------------");

                PricingResult pricingResult = pricingProblem.Solve(
                    data,
                    masterResult.Duals);

                Console.WriteLine($"Optimal      = {pricingResult.IsOptimal}");

                Console.WriteLine($"Reduced Cost = {pricingResult.ReducedCost:F6}");

                Console.WriteLine(
                    $"Iteration {iteration} Reduced Cost = {pricingResult.ReducedCost:F6}");

                Console.WriteLine($"Candidate Pattern = {pricingResult.Pattern}");

                //
                // Stop?
                //
                if (pricingResult.ReducedCost >= 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("No improving pattern found.");

                    Console.WriteLine("Column generation terminates.");

                    break;
                }

                //
                // Add Pattern
                //
                Pattern newPattern = new Pattern(
                    nextPatternId++,
                    1,
                    pricingResult.Pattern.Fill);

                patterns.Add(newPattern);

                Console.WriteLine();
                Console.WriteLine($"Added Pattern {newPattern.Id}");

                Console.WriteLine(newPattern);

                Console.WriteLine($"Pattern Count = {patterns.Count}");

                iteration++;
            }

            //
            // STEP 4 - Solve Integer Master
            //
            Console.WriteLine();
            Console.WriteLine("STEP 4 - Solve Integer Master");
            Console.WriteLine("-----------------------------");

            MasterResult finalMasterResult = masterProblem.Solve(
                data,
                patterns,
                isFinalSolve: true);

            Console.WriteLine();
            Console.WriteLine("Final Integer Master Solution");
            Console.WriteLine("=============================");

            Console.WriteLine($"Optimal   = {finalMasterResult.IsOptimal}");

            Console.WriteLine($"Objective = {finalMasterResult.ObjectiveValue:F3}");

            PrintMasterSolution(
                data,
                patterns,
                finalMasterResult);

            Console.WriteLine();
        }

        private void PrintPatterns(List<Pattern> patterns)
        {
            Console.WriteLine();
            Console.WriteLine("Current Patterns");
            Console.WriteLine("----------------");

            foreach (Pattern pattern in patterns)
            {
                Console.WriteLine(pattern);
            }
        }

        private void PrintMasterModel(
            CuttingStockData data,
            List<Pattern> patterns)
        {
            Console.WriteLine();
            Console.WriteLine("Current Master LP");
            Console.WriteLine("-----------------");

            Console.Write("Minimize: ");

            for (int p = 0; p < patterns.Count; p++)
            {
                Console.Write($"x{p}");

                if (p < patterns.Count - 1)
                {
                    Console.Write(" + ");
                }
            }

            Console.WriteLine();
            Console.WriteLine();

            Console.WriteLine("Subject To:");

            for (int item = 0; item < data.NumberOfItems; item++)
            {
                for (int p = 0; p < patterns.Count; p++)
                {
                    Console.Write($"{patterns[p].Fill[item]}x{p}");

                    if (p < patterns.Count - 1)
                    {
                        Console.Write(" + ");
                    }
                }

                Console.WriteLine($" >= {data.Demands[item]}");
            }

            Console.WriteLine();
            Console.WriteLine("x[p] >= 0");
        }

        private void PrintMasterSolution(
            CuttingStockData data,
            List<Pattern> patterns,
            MasterResult result)
        {
            Console.WriteLine();
            Console.WriteLine("Master Solution");
            Console.WriteLine("===============");

            Console.WriteLine();
            Console.WriteLine($"Objective = {result.ObjectiveValue:F3}");

            Console.WriteLine();
            Console.WriteLine("Variable Values");
            Console.WriteLine("---------------");

            for (int p = 0; p < patterns.Count; p++)
            {
                Console.WriteLine(
                    $"x{p} = {result.CutValues[p],8:F3}   {patterns[p]}");
            }

            Console.WriteLine();
            Console.WriteLine("Constraint Analysis");
            Console.WriteLine("-------------------");

            for (int item = 0; item < data.NumberOfItems; item++)
            {
                Console.WriteLine();
                Console.WriteLine($"Demand Constraint {item}");

                Console.WriteLine($"  Item Width = {data.ItemSizes[item]}");

                Console.WriteLine();

                Console.Write("Activity = ");

                bool first = true;

                for (int p = 0; p < patterns.Count; p++)
                {
                    if (!first)
                    {
                        Console.Write(" + ");
                    }

                    Console.Write(
                        $"{patterns[p].Fill[item]}*{result.CutValues[p]:F3}");

                    first = false;
                }

                Console.WriteLine();

                double activity = result.ProducedAmounts[item];

                double demand = data.Demands[item];

                double slack = activity - demand;

                Console.WriteLine($"Produced = {activity:F3}");

                Console.WriteLine($"Demand   = {demand:F3}");

                Console.WriteLine($"Slack    = {slack:F3}");

                Console.WriteLine($"Dual     = {result.Duals[item]:F6}");

                if (Math.Abs(slack) < 1e-6)
                {
                    Console.WriteLine("Status   = TIGHT");
                }
                else
                {
                    Console.WriteLine("Status   = NON-BINDING");
                }
            }
        }
    }
}
