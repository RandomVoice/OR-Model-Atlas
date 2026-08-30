using System.Collections.Generic;

using CuttingStockLearning.Models;

using ILOG.Concert;
using ILOG.CPLEX;

namespace CuttingStockLearning.Master
{
    public class MasterProblem
    {
        public MasterResult Solve(
            CuttingStockData data,
            List<Pattern> patterns,
            bool isFinalSolve = false)
        {
            using var cplex = new Cplex();

            //
            // Decision Variables
            //
            INumVar[] cut;

            if (isFinalSolve)
            {
                cut = cplex.IntVarArray(
                    patterns.Count,
                    0,
                    int.MaxValue);
            }
            else
            {
                cut = cplex.NumVarArray(
                    patterns.Count,
                    0,
                    double.MaxValue);
            }

            //
            // Objective
            //
            ILinearNumExpr objective = cplex.LinearNumExpr();

            for (int p = 0; p < patterns.Count; p++)
            {
                objective.AddTerm(
                    patterns[p].Cost,
                    cut[p]);
            }

            cplex.AddMinimize(objective);

            //
            // Demand Constraints
            //
            IRange[] demandConstraints =
                new IRange[data.NumberOfItems];

            for (int item = 0; item < data.NumberOfItems; item++)
            {
                ILinearNumExpr expr = cplex.LinearNumExpr();

                for (int p = 0; p < patterns.Count; p++)
                {
                    expr.AddTerm(
                        patterns[p].Fill[item],
                        cut[p]);
                }

                demandConstraints[item] = cplex.AddGe(
                    expr,
                    data.Demands[item]);
            }

            //
            // Solve
            //
            if (!cplex.Solve())
            {
                return new MasterResult
                {
                    IsOptimal = false
                };
            }

            //
            // Extract Dual Values
            //
            double[] duals = new double[data.NumberOfItems];

            if (!isFinalSolve)
            {
                for (int item = 0; item < data.NumberOfItems; item++)
                {
                    duals[item] = cplex.GetDual(
                        demandConstraints[item]);
                }
            }

            //
            // Extract Variable Values
            //
            double[] cutValues = new double[patterns.Count];

            for (int p = 0; p < patterns.Count; p++)
            {
                cutValues[p] = cplex.GetValue(
                    cut[p]);
            }

            //
            // Compute Production Levels
            //
            double[] producedAmounts =
                new double[data.NumberOfItems];

            for (int item = 0; item < data.NumberOfItems; item++)
            {
                double produced = 0;

                for (int p = 0; p < patterns.Count; p++)
                {
                    produced +=
                        patterns[p].Fill[item]
                        * cutValues[p];
                }

                producedAmounts[item] = produced;
            }

            //
            // Package Results
            //
            return new MasterResult
            {
                IsOptimal = true,

                ObjectiveValue =
                    cplex.ObjValue,

                Duals =
                    duals,

                CutValues =
                    cutValues,

                ProducedAmounts =
                    producedAmounts
            };
        }
    }
}
