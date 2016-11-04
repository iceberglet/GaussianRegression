﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Distributions;
using GaussianRegression.Core;

namespace GaussianRegression
{
    static class Test
    {
        static Random rand = new Random();


        public static void testSimple()
        {
            List<XYPair> values = new List<XYPair>();
            List<Vector<double>> list_x = new List<Vector<double>>();
            for(int i = 1; i < 500; i++)
            {
                double xx = i / 10.0;
                Vector<double> x = Utility.V(xx);
                list_x.Add(x);
                if (i % 20 == 0)
                    values.Add(new XYPair(x, xx * xx + (rand.NextDouble() - 1) * 500));
            }

            CovFunction cf = CovFunction.SquaredExponential(8, 50) + CovFunction.GaussianNoise(30);
            
            GP myGP = new GP(sampledValues: values, list_x: list_x.ToList(), cov_f: cf,
                heteroscedastic: true,
                lengthScale: 60, sigma_f: 20);
            var res = myGP.predict();

            FileService fs = new FileService("Test.csv");

            fs.writeToFile(FileService.convertGPResult(res, values));
        }

        public static void testMotor()
        {
            List<XYPair> values = FileService.readFromFile("Motor.txt");
        }


        public static void testComplex()
        {
            Func<double, double> f_pure = x => -(x - 134) * (x - 167) / 100.0 + 250;
            Func<double, double> f_sd = x => 60 + Math.Exp(x / 100);
            Func<double, double> f = x => f_pure(x) + Normal.InvCDF(0, f_sd(x), rand.NextDouble());

            int size = 600;
            List<XYPair> sampled = new List<XYPair>();
            List<XYPair> xyPairs = new List<XYPair>();
            Vector<double>[] list_x = new Vector<double>[size];

            for (int i = 0; i < size; ++i)
            {
                Vector<double> newX = Utility.V(i);

                list_x[i] = newX;
                double y = f(i);
                XYPair newPair = new XYPair(newX, y);

                xyPairs.Add(newPair);
                if (rand.NextDouble() < 0.15)
                    sampled.Add(newPair);
            }
            CovFunction cf = CovFunction.SquaredExponential(40, 10) + CovFunction.GaussianNoise(10);
            
            GP myGP = new GP(sampledValues: sampled, list_x: list_x.ToList(), cov_f: cf,
                heteroscedastic: true,
                lengthScale: 40, sigma_f: 60);
            var res = myGP.predict();

            FileService fs = new FileService("Test.csv");
            
            /*
            CovMatrix covMatrix = new CovMatrix(cf, sampled);
            Dictionary<Vector<double>, NormalDistribution> res = new Dictionary<Vector<double>, NormalDistribution>();
            xyPairs.ForEach(xy => {
                res.Add(xy.x, covMatrix.getPosterior(xy.x));
            });*/

            fs.writeToFile(FileService.convertGPResult(res, sampled));
        }


        public static void testCovFunc()
        {
            CovFunction cf2 = CovFunction.SquaredExponential(10, 2);
            //CovFunction cf2 = CovFunction.GaussianNoise(10);

            Vector<double> a = Vector<double>.Build.Dense(new double[] { 1, 3, 5, 7 });
            Vector<double> a_prime = Vector<double>.Build.Dense(new double[] { 1, 3, 5, 7 });
            Vector<double> b = Vector<double>.Build.Dense(new double[] { 2, 4, 6, 8 });
            Vector<double> c = Vector<double>.Build.Dense(new double[] { 2, 4, 6, 8, 10 });

            Console.WriteLine("a and a_prime gives: " + cf2.eval(a, a_prime));
            Console.WriteLine("a and b gives: " + cf2.eval(a, b));
            Console.WriteLine("a and c gives: " + cf2.eval(a, c));
            

        }
    }
}