﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Distributions;

namespace GaussianRegression.Core
{
    //CovMatrix which has input-dependent noises
    //that is obtained via another GP process for its noise
    class CovMatrixHetero : CovMatrix
    {
        CovMatrix matrixForNoise;
        CovFunction cf_noise;
        double indicativeSd;

        public CovMatrixHetero(CovFunction cf, List<XYPair> list_xy = null, double sigma_f = 1, double delta = 0.0005) : base(cf, list_xy, delta)
        {
            this.indicativeSd = sigma_f;

            //var ls = cf.param.ContainsKey(typeof(LengthScale)) ? (LengthScale)cf.param[typeof(LengthScale)] : new LengthScale(10) ;
            var ls = (LengthScale)cf.param[typeof(LengthScale)];

            //By design, we use squared exponential for GP_1 that estimates the error term.
            this.cf_noise = CovFunction.SquaredExponential(new LengthScale(ls.value), new SigmaF(this.indicativeSd)) + CovFunction.GaussianNoise(new SigmaJ(0.01));
            var initialNoise = list_xy.Select(xy => new XYPair(xy.x, 0)).ToList();
            var diag = Enumerable.Repeat(0d, list_xy.Count).ToArray();
            matrixForNoise = null;
            K_diag = Matrix<double>.Build.Diagonal(diag);
            K_base = K_base;
            this.updateNoise_z();
            //this.performNoiseAnalysis();
        }

        //Add noise evaluation process
        public override void addX(XYPair pair)
        {
            base.addX(pair);
            this.updateNoise_z();
            this.updateNoise(noise_z);
        }

        public void optimize()
        {
            this.performNoiseAnalysis();
        }

        private static readonly int MAX_HETEROSCEDASTIC_ITERATION = 100;
        private static readonly int HETEROSCEDASTIC_POINT_SAMPLE_SIZE = 20;
        private static readonly double HETEROSCEDASTIC_CONVERGENCE_PERCENTAGE = 0.003;
        List<XYPair> noise_z = new List<XYPair>();

        private void updateNoise_z()
        {
            noise_z = new List<XYPair>();  //Note: the y here refers to the noise term
            List<XYPair> knownPoints = this.xyPairs.ToList();

            Dictionary<Vector<double>, NormalDistribution> dictForSampled = new Dictionary<Vector<double>, NormalDistribution>();
            knownPoints.ForEach(x => {
                dictForSampled.Add(x.x, this.getPosterior(x.x));
            });

            foreach (XYPair xyPair in knownPoints)
            {
                NormalDistribution nd = dictForSampled[xyPair.x];  // current estimate
                double varEstimate = 0;

                for (int i = 0; i < HETEROSCEDASTIC_POINT_SAMPLE_SIZE; i++)
                {
                    double sample = Normal.InvCDF(nd.mu, nd.sd, GPUtility.NextProba());
                    varEstimate += Math.Pow((xyPair.y - sample), 2);
                }
                varEstimate *= 0.5 / HETEROSCEDASTIC_POINT_SAMPLE_SIZE;
                varEstimate = Math.Sqrt(varEstimate);   //Back to SD

                //the new GP is performed on the logarithm of SD - so the SD is always positive
                varEstimate = Math.Log(varEstimate);
                noise_z.Add(new XYPair(xyPair.x, varEstimate));
            }
        }
        

        //Using Most Likely Heteroscedastic Approach
        //http://www.machinelearning.org/proceedings/icml2007/papers/326.pdf
        public void performNoiseAnalysis()
        {
            int counter = 0;
            bool optimized = false;
            double previousNoiseSum = 0;
            bool converged = false;
            Dictionary<Vector<double>, NormalDistribution> resulting_z = new Dictionary<Vector<double>, NormalDistribution>();

            while (counter < MAX_HETEROSCEDASTIC_ITERATION && !converged)
            {
                GPUtility.Log("Heteroscedastic Iter: " + counter, GPUtility.LogLevel.DEBUG);

                //Noise z should be updated
                this.updateNoise_z();

                var nextNoiseSum = noise_z.Sum(n => n.y * n.y);
                var currentError = Math.Abs(previousNoiseSum - nextNoiseSum) / nextNoiseSum;
                if (currentError < HETEROSCEDASTIC_CONVERGENCE_PERCENTAGE)
                {
                    converged = true;
                }
                else
                {
                    previousNoiseSum = nextNoiseSum;
                }
                GPUtility.Log("Current Error " + currentError + " Prev: " + previousNoiseSum + " Curr: " + nextNoiseSum, GPUtility.LogLevel.DEBUG);

                //2. Construct another Gaussian CovMatrix to evaluate noise
                //********** TODO: alter the covariance func instead of using the given one!
                matrixForNoise = new CovMatrix(cf_noise, noise_z, delta);
                if (optimized)
                {
                    ModelOptimizer mo = new ModelOptimizer(matrixForNoise, cf_noise, null, null);
                    mo.optimize();
                    cf_noise.param[typeof(SigmaJ)] = new SigmaJ(0.01d);
                    optimized = true;
                }
                //3. Update the diagonal matrices for this

                this.updateNoise(noise_z);
                
                counter++;
            }
        }
        
        //For Debug Purpose Only
        public void evaluateHeteroResult(List<LabeledVector> list_x)
        {
            if (matrixForNoise == null)
                return;
            //Design points
            var varPredictions = new Dictionary<LabeledVector, double>();
            list_x.ForEach(x => varPredictions.Add(x, matrixForNoise.getPosterior(x.x).mu));
            FileService fs = new FileService("VarResult.csv");
            var listString = new List<string>();
            //Sampled points
            xyPairs.ToList().ForEach(xy => {
                double est = this.getPosterior(xy.x).mu;
                listString.Add(xy.x.toString() + "," + Math.Abs(xy.y - est));
            });
            varPredictions.Keys.ToList().ForEach(vect => listString.Add(vect.x.toString() + ",," + Math.Exp(varPredictions[vect])));
            noise_z.ForEach(xy => listString.Add(xy.x.toString() + ",,," + Math.Exp(xy.y)));
            fs.writeToFile(listString.ToArray());
        }

        //Updates the K_diag and predicted noise term for each point
        private void updateNoise(List<XYPair> noise_z)
        {
            Vector<double>[] xInSample = xyPairs.Select(pair => pair.x).ToArray();

            double[,] k_diag = K_diag == null ? new double[K_base.RowCount, K_base.ColumnCount] : K_diag.ToArray();

            foreach (XYPair noise in noise_z)
            {
                int idx = Array.IndexOf(xInSample, noise.x);
                if (idx > -1)
                    k_diag[idx, idx] = Math.Exp(noise.y);
            }
            K_diag = Matrix<double>.Build.DenseOfArray(k_diag);
            K_base = K_base;
            return;
        }

        public override NormalDistribution getPosterior(Vector<double> x_0)
        {
            var res = base.getPosterior(x_0);
            //The R term in machine learning paper eq.(2)
            var moreSD = matrixForNoise == null? 0 : Math.Exp(matrixForNoise.getPosterior(x_0).mu);     //Notice the Exp
            return new NormalDistribution(res.mu, Math.Sqrt(res.sd * res.sd + moreSD * moreSD));
        }


    }
}
