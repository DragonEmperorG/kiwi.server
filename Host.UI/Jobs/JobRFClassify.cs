﻿using Engine.Brain.AI.ML;
using Engine.GIS.GLayer.GRasterLayer;
using Engine.GIS.GOperation.Tools;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace Host.UI.Jobs
{
    public class JobRFClassify : IJob
    {

        public string Name => "RFClassificationTask";

        public string Summary { get; private set; } = "";

        public double Process { get; private set; } = 0.0;

        public DateTime StartTime { get; private set; } = DateTime.Now;

        public PlotModel[] PlotModels => throw new NotImplementedException();
        /// <summary>
        /// 
        /// </summary>
        public bool Complete { get; private set; } = false;
        /// <summary>
        /// 
        /// </summary>
        public event OnTaskCompleteHandler OnTaskComplete;
        /// <summary>
        /// 
        /// </summary>
        Thread _t;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="treeCount"></param>
        /// <param name="fullFilename"></param>
        /// <param name="rasterLayer"></param>
        public JobRFClassify(int treeCount, string fullFilename, GRasterLayer rasterLayer)
        {
            _t = new Thread(() =>
            {
                RF rf = new RF(treeCount);
                //training
                Summary = "随机森林训练中";
                using (StreamReader sr = new StreamReader(fullFilename))
                {
                    List<List<double>> inputList = new List<List<double>>();
                    List<int> outputList = new List<int>();
                    string text = sr.ReadLine().Replace("\t",",");
                    do
                    {
                        string[] rawdatas = text.Split(',');
                        outputList.Add(Convert.ToInt32(rawdatas.Last()));
                        List<double> inputItem = new List<double>();
                        for (int i = 0; i < rawdatas.Length - 1; i++)
                            inputItem.Add(Convert.ToDouble(rawdatas[i]));
                        inputList.Add(inputItem);
                        text = sr.ReadLine();
                    } while (text != null);
                    double[][] inputs = new double[inputList.Count][];
                    int[] outputs = outputList.ToArray();
                    for (int i = 0; i < inputList.Count; i++)
                        inputs[i] = inputList[i].ToArray();
                    rf.Train(inputs, outputs);
                }
                //image classify
                Summary = "分类应用中";
                IRasterLayerCursorTool pRasterLayerCursorTool = new GRasterLayerCursorTool();
                pRasterLayerCursorTool.Visit(rasterLayer);
                //GDI graph
                Bitmap classificationBitmap = new Bitmap(rasterLayer.XSize, rasterLayer.YSize);
                Graphics g = Graphics.FromImage(classificationBitmap);
                //
                int seed = 0;
                int totalPixels = rasterLayer.XSize * rasterLayer.YSize;
                Process = 0.0;
                //应用dqn对图像分类
                for (int i = 0; i < rasterLayer.XSize; i++)
                    for (int j = 0; j < rasterLayer.YSize; j++)
                    {
                        //get normalized input raw value
                        double[] raw = pRasterLayerCursorTool.PickRawValue(i, j);
                        double[][] inputs = new double[1][];
                        inputs[0] = raw;
                        //}{debug
                        int[] ouputs = rf.Predict(inputs);
                        int gray = ouputs[0];
                        //convert action to raw byte value
                        Color c = Color.FromArgb(gray, gray, gray);
                        Pen p = new Pen(c);
                        SolidBrush brush = new SolidBrush(c);
                        g.FillRectangle(brush, new Rectangle(i, j, 1, 1));
                        //report progress
                        Process = (double)seed++ / totalPixels;
                    }
                //保存结果至tmp
                string fullFileName = Directory.GetCurrentDirectory() + @"\tmp\" + DateTime.Now.ToFileTimeUtc() + ".png";
                classificationBitmap.Save(fullFileName);
                //rf complete
                Summary = "RF训练分类完成";
                Complete = true;
                OnTaskComplete?.Invoke(Name, fullFileName);
            });
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="paramaters"></param>
        public void Start()
        {
            StartTime = DateTime.Now;
            _t.IsBackground = true;
            _t.Start();
        }
    }
}
