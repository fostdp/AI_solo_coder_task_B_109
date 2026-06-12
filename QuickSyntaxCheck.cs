using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// 快速语法验证脚本 - 用于验证新增服务的基本语法正确性
// 执行方式: csc QuickSyntaxCheck.cs

namespace ClayMonitor.QuickCheck
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("=== 新增功能快速语法验证 ===");
            Console.WriteLine();

            try
            {
                TestPenetrationCalculations();
                Console.WriteLine("✓ 渗透深度预测算法验证通过");

                TestGibbsFreeEnergy();
                Console.WriteLine("✓ Gibbs自由能计算验证通过");

                TestArrheniusRate();
                Console.WriteLine("✓ Arrhenius速率方程验证通过");

                TestLucasWashburn();
                Console.WriteLine("✓ Lucas-Washburn方程验证通过");

                TestBreathabilityAnalysis();
                Console.WriteLine("✓ 呼吸性分析算法验证通过");

                Console.WriteLine();
                Console.WriteLine("=== 所有算法验证通过 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ 验证失败: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void TestPenetrationCalculations()
        {
            // 测试1: TEOS在标准条件下的渗透
            double t = 3600;           // 1小时
            double r = 500e-9;          // 500nm
            double gamma = 0.0235;      // TEOS表面张力
            double theta = 95 * Math.PI / 180;
            double eta = 0.00085;       // TEOS粘度
            double phi = 0.35;          // 孔隙率

            double depth = CalculateLucasWashburn(t, r, gamma, theta, eta, phi);
            
            if (depth < 4.0 || depth > 8.0)
                throw new Exception($"渗透深度计算异常: {depth:F2}mm，预期4-8mm");

            double Pc = 2 * gamma * Math.Cos(theta) / r;
            if (Math.Abs(Pc - 1456) > 200)
                throw new Exception($"毛细管压力计算异常: {Pc:F0}Pa，预期约1450Pa");
        }

        static double CalculateLucasWashburn(double t, double r, double gamma, double theta, double eta, double phi)
        {
            if (t <= 0) return 0;
            double cosTheta = Math.Cos(theta);
            if (cosTheta <= 0) return 0;
            double hSquared = (gamma * cosTheta * r * t) / (2 * eta * phi);
            return Math.Sqrt(Math.Max(0, hSquared)) * 1000;
        }

        static void TestGibbsFreeEnergy()
        {
            double deltaH = -125600;  // -125.6 kJ/mol
            double deltaS = 85.2;     // 85.2 J/mol·K
            double T = 298.15;        // 25℃

            double deltaG = deltaH - T * deltaS;
            double deltaG_kJ = deltaG / 1000;

            if (deltaG >= 0)
                throw new Exception($"Gibbs自由能应该为负，实际: {deltaG_kJ:F2} kJ/mol");

            if (deltaG_kJ < -160 || deltaG_kJ > -90)
                throw new Exception($"Gibbs自由能计算异常: {deltaG_kJ:F2} kJ/mol，预期-90~-160 kJ/mol");

            double R = 8.314;
            double K = Math.Exp(-deltaG / (R * T));
            if (K < 1e15 || K > 1e20)
                throw new Exception($"平衡常数计算异常: {K:E2}");
        }

        static void TestArrheniusRate()
        {
            double A = 1.25e6;
            double Ea = 68500;  // 68.5 kJ/mol
            double T = 298.15;
            double R = 8.314;

            double k = A * Math.Exp(-Ea / (R * T));

            if (k < 1e-5 || k > 1e-2)
                throw new Exception($"速率常数计算异常: {k:E2}");
        }

        static void TestLucasWashburn()
        {
            // 验证量纲分析
            double t = 3600;           // s
            double r = 1e-6;           // m
            double gamma = 0.072;      // N/m (= kg/s²)
            double theta = 0;          // rad
            double eta = 0.001;        // Pa·s (= kg/(m·s))
            double phi = 0.4;          // 无量纲

            double h = CalculateLucasWashburn(t, r, gamma, theta, eta, phi);
            
            // 结果应该以mm为单位，且合理
            if (h < 0)
                throw new Exception("渗透深度不能为负");
            
            // 验证 t=0 时深度为0
            double h0 = CalculateLucasWashburn(0, r, gamma, theta, eta, phi);
            if (Math.Abs(h0) > 1e-10)
                throw new Exception("t=0时渗透深度应该为0");
            
            // 验证接触角大于90度时不渗透
            double h_nospread = CalculateLucasWashburn(t, r, gamma, Math.PI * 0.6, eta, phi);
            if (h_nospread > 0)
                throw new Exception("接触角大于90度时不应该渗透");
        }

        static void TestBreathabilityAnalysis()
        {
            // 生成模拟的日周期温湿度数据
            int n = 50;
            double[] T = new double[n];
            double[] RH = new double[n];
            DateTime[] times = new DateTime[n];
            DateTime now = DateTime.Now;

            for (int i = 0; i < n; i++)
            {
                double phase = (i / 48.0) * 2 * Math.PI;  // 48个点一个周期
                T[i] = 22 + 4 * Math.Sin(phase - Math.PI / 4);  // 温度波动±4℃
                RH[i] = 55 - 8 * Math.Sin(phase);                // 湿度波动±8%
                times[i] = now.AddMinutes(-30 * i);
            }

            // 简单验证
            double tempAmp = T.Max() - T.Min();
            if (tempAmp < 6)
                throw new Exception($"温度波幅异常: {tempAmp:F1}℃");

            double humAmp = RH.Max() - RH.Min();
            if (humAmp < 12)
                throw new Exception($"湿度波幅异常: {humAmp:F1}%");

            // 测试互相关
            double[] normT = Normalize(T);
            double[] normH = Normalize(RH);
            double corr = CalculateCrossCorrelation(normT, normH, 6);
            
            // 温湿度应该负相关
            if (corr > -0.3)
                throw new Exception($"温湿度相关性异常: {corr:F2}，预期负相关");
        }

        static double[] Normalize(double[] data)
        {
            double mean = data.Average();
            double std = Math.Sqrt(data.Sum(x => (x - mean) * (x - mean)) / data.Length);
            if (std == 0) return data.Select(_ => 0.0).ToArray();
            return data.Select(x => (x - mean) / std).ToArray();
        }

        static double CalculateCrossCorrelation(double[] x, double[] y, int lag)
        {
            int n = x.Length;
            double sum = 0;
            int count = 0;
            for (int i = Math.Max(0, lag); i < Math.Min(n, n + lag); i++)
            {
                int j = i - lag;
                if (j >= 0 && j < n)
                {
                    sum += x[i] * y[j];
                    count++;
                }
            }
            return count > 0 ? sum / count : 0;
        }
    }
}
