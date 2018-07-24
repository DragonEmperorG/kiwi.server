﻿using Engine.Brain.AI.RL;
using Engine.Brain.Bootstrap;
using Engine.Brain.Entity;
using Engine.Brain.Utils;
using Engine.GIS.Entity;
using Engine.GIS.GLayer.GRasterLayer;
using Engine.GIS.GLayer.GRasterLayer.GBand;
using Engine.GIS.GOperation.Arithmetic;
using Host.Image.UI.SettingForm;
using Host.Image.UI.SettingForm.SLIC;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TensorFlow;

namespace Host.Image.UI
{
    /// <summary>
    /// 标识颜色
    /// </summary>
    public enum STATUE_ENUM
    {
        ERROR = 0,
        NORMAL = 1,
        WARNING = 2
    }

    public partial class Main : Form
    {

        #region 初始化

        public Main()
        {
            InitializeComponent();
        }

        #endregion

        #region 缓存管理
        /// <summary>
        /// raster layer
        /// </summary>
        Dictionary<string, GRasterLayer> _rasterDic = new Dictionary<string, GRasterLayer>();
        /// <summary>
        /// 管理全局的图像与树视图区域的缓存对应
        /// key是图片名称或图+波段名称，值为对应的bitmap
        /// </summary>
        Dictionary<string, Bitmap2> _imageDic = new Dictionary<string, Bitmap2>();
        /// <summary>
        /// 当前选中的Bitmap2信息，包含图层，波段，索引等
        /// </summary>
        Bitmap2 _selectBmp2 = null;

        #endregion

        #region 算法执行
        /// <summary>
        /// 应用深度学习模型进行分类
        /// </summary>
        /// <param name="rasterLayer"></param>
        private void RunClassify(GRasterLayer rasterLayer, bool useSLIC, string pbName, string centerName, string labelName)
        {
            //判断图层结构，选用不同的tensor输入
            ShapeEnum shapeEuum;
            if (rasterLayer.BandCount == 130)
                shapeEuum = ShapeEnum.THIRTEEN_TEN;
            else if (rasterLayer.BandCount == 100)
                shapeEuum = ShapeEnum.TEN_TEN;
            else if (rasterLayer.BandCount == 64)
                shapeEuum = ShapeEnum.EIGHT_EIGHT;
            else
                shapeEuum = ShapeEnum.TEN_TEN;
            //构建结果图层，用于动态绘制
            Bitmap bmp = new Bitmap(rasterLayer.XSize, rasterLayer.YSize);
            string nodeName = rasterLayer.Name + "结果图层";
            TreeNode childrenNode = new TreeNode(nodeName);
            _imageDic.Add(nodeName, new Bitmap2(name: nodeName, bmp: bmp));
            Invoke(new UpdateTreeNodeHandler(UpdateTreeNode), null, childrenNode);
            //获取波段
            TensorflowBootstrap model = new TensorflowBootstrap(pbName);
            //判断是否基于超像素
            if (!useSLIC)
            {
                for (int i = 0; i < rasterLayer.XSize; i++)
                    for (int j = 0; j < rasterLayer.YSize; j++)
                    {
                        float[] input = rasterLayer.GetPixelFloat(i, j).ToArray();
                        long classified = model.Classify(input, shapeEuum);
                        Invoke(new PaintPointHandler(PaintPoint), bmp, i, j, Convert.ToByte(classified * 15));
                        Invoke(new UpdateStatusLabelHandler(UpdateStatusLabel), "应用分类中，总进度：" + i + "列" + j + "行", STATUE_ENUM.WARNING);
                    }
            }
            else
            {
                //基于slic的超像素绘制方法
                using (StreamReader sr_center = new StreamReader(centerName))
                using (StreamReader sr_label = new StreamReader(labelName))
                {
                    Center[] centers = SuperPixelSegment.ReadCenter(sr_center.ReadToEnd());
                    Bitplane labels = SuperPixelSegment.ReadLabel(sr_label.ReadToEnd());
                    int[] mask = new int[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 };
                    for (int i = 0; i < centers.Length; i++)
                    {
                        Center center = centers[i];
                        float[] input = rasterLayer.GetPixelFloatWidthConv((int)center.X, (int)center.Y, mask).ToArray();
                        long classified = model.Classify(input, shapeEuum);
                        center.L = classified * 15;
                        center.A = classified * 15;
                        center.B = classified * 15;
                        Invoke(new UpdateStatusLabelHandler(UpdateStatusLabel), "已处理第" + i + "/" + centers.Length + "个中心", STATUE_ENUM.WARNING);
                    }
                    //遍历图片进行绘制
                    for (int i = 0; i < rasterLayer.XSize; i++)
                        for (int j = 0; j < rasterLayer.YSize; j++)
                            Invoke(new PaintPointHandler(PaintPoint), bmp, i, j, Convert.ToByte(centers[(int)Math.Floor(labels.GetPixel(i, j))].L));
                }
            }
        }
        /// <summary>
        /// }{小萌娃记得改哟
        /// </summary>
        /// <param name="fileNameCollection"></param>
        /// <param name="centers"></param>loul
        private void RunCenter(List<string> fileNameCollection, Center[] centers)
        {
            DataTable dt = new DataTable();
            int count = fileNameCollection.Count;
            for (int r = 0; r <= centers.Length; r++)
                dt.Rows.Add(dt.NewRow());
            for (int k = 0; k < count; k++)
            {
                Invoke(new UpdateStatusLabelHandler(UpdateStatusLabel), "应用超像素中心计算开始，进度 " + k + "/" + fileNameCollection.Count, STATUE_ENUM.WARNING);
                string fileName = fileNameCollection[k];
                Bitmap bmp = new Bitmap(fileName);
                //添加1列
                DataColumn dc = dt.Columns.Add(System.IO.Path.GetFileNameWithoutExtension(fileName));
                dt.Rows[0][dc] = System.IO.Path.GetFileNameWithoutExtension(fileName);
                //1.提取x,y位置的像素值
                for (int i = 0; i < centers.Length; i++)
                    //}{小萌娃记得改哟  new int[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 } 是 3x3 mask 都是1的算子，这里可以修改 自动识别的
                    dt.Rows[i + 1][dc] = GConvolution.Run(bmp, (int)centers[i].X, (int)centers[i].Y, new int[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 });
            }
            Invoke(new SaveExcelHandler(SaveExcel), dt);
        }
        /// <summary>
        /// 超像素计算
        /// </summary>
        /// <param name="bmp"></param>
        private void RunSLIC(Bitmap bmp)
        {
            Invoke(new UpdateStatusLabelHandler(UpdateStatusLabel), "超像素中心计算开始...", STATUE_ENUM.WARNING);
            SlicPackage pkg = SuperPixelSegment.Run(bmp, 10000, 3, Color.White);
            Invoke(new SaveJsonHandler(SaveJson), pkg.CENTER);
            Invoke(new SaveJsonHandler(SaveJson), pkg.Label);
            Invoke(new SaveBitmapHandler(SaveBitmap), pkg.Edge);
            Invoke(new SaveBitmapHandler(SaveBitmap), pkg.Average);
        }

        private void RunDQN(GRasterLayer featureRasterLayer, GRasterLayer labelRasterLayer)
        {
            IDEnv env = new DImageEnv(featureRasterLayer, labelRasterLayer);
            DQN dqn = new DQN(env);
            dqn.OnLearningLossEventHandler += Dqn_OnLearningLossEventHandler;
            dqn.Learn();
            //
            Bitmap bmp = new Bitmap(featureRasterLayer.XSize, featureRasterLayer.YSize);
            //应用dqn对图像分类
            for (int i = 0; i < featureRasterLayer.XSize; i++)
                for (int j = 0; j < featureRasterLayer.YSize; j++)
                {
                    float[] raw = featureRasterLayer.GetPixelFloat(i, j).ToArray();
                    float[] normal = NP.Normalize(raw, 255f);
                    int action; float q;
                    (action, q) = dqn.ChooseAction(normal);
                    Invoke(new PaintPointHandler(PaintPoint), bmp, i, j, Convert.ToByte(action * 10));
                }
        }

        private void Dqn_OnLearningLossEventHandler(float loss, float totalReward, float accuracy, float progress, string epochesTime)
        {
            accuracy *= 100;
            progress *= 100;
            string msg = string.Format("time:{0},progress:{1}%;loss:{2},reward:{3},accuracy:{4}%", epochesTime, progress, loss, totalReward, accuracy);
            Invoke(new UpdateMapListBoxHandler(UpdateMapListBox), msg);
        }

        #endregion

        #region 界面更新

        /// <summary>
        /// 保存json结果
        /// </summary>
        /// <param name="jsonText"></param>
        private void SaveJson(string jsonText)
        {
            UpdateStatusLabel("保存Json结果文件");
            SaveFileDialog sfg = new SaveFileDialog
            {
                DefaultExt = ".json"
            };
            if (sfg.ShowDialog() == DialogResult.OK)
            {
                using (StreamWriter sw = new StreamWriter(sfg.FileName))
                {
                    sw.Write(jsonText);
                }
            }
        }
        /// <summary>
        /// 保存bmp位图结果
        /// </summary>
        /// <param name="bmp"></param>
        private void SaveBitmap(Bitmap bmp)
        {
            UpdateStatusLabel("保存.jpg结果文件");
            SaveFileDialog sfg = new SaveFileDialog();
            sfg.DefaultExt = ".jpg";
            if (sfg.ShowDialog() == DialogResult.OK)
                bmp.Save(sfg.FileName);
        }
        /// <summary>
        /// 保存Excel结果
        /// </summary>
        /// <param name="dt"></param>
        private void SaveExcel(DataTable dt)
        {
            SaveFileDialog sfg = new SaveFileDialog
            {
                DefaultExt = ".xls"
            };
            if (sfg.ShowDialog() == DialogResult.OK)
            {
                using (var excel = new ExcelPackage(new FileInfo(sfg.FileName)))
                {
                    var ws = excel.Workbook.Worksheets.Add("Sheet1");
                    for (int r = 0; r < dt.Rows.Count; r++)
                    {
                        for (int c = 0; c < dt.Columns.Count; c++)
                        {
                            ws.Cells[r + 1, c + 1].Value = dt.Rows[r][c];
                        }
                    }
                    excel.Save();
                }
            }
        }
        /// <summary>
        /// 更新底部栏提示
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="statue"></param>
        private void UpdateStatusLabel(string msg, STATUE_ENUM statue = STATUE_ENUM.NORMAL)
        {
            if (statue == STATUE_ENUM.ERROR)
            {
                map_statusLabel.Image = Properties.Resources.smile_sad_64;
                map_statusLabel.ForeColor = Color.Red;
            }
            else if (statue == STATUE_ENUM.WARNING)
            {
                map_statusLabel.Image = Properties.Resources.smile_sad_64;
                map_statusLabel.ForeColor = Color.Red;
            }
            else
            {
                map_statusLabel.Image = Properties.Resources.smile_64;
                map_statusLabel.ForeColor = Color.Black;
            }
            //
            map_statusLabel.Text = msg;
        }
        /// <summary>
        /// 更新地图相关树视图节点内容
        /// </summary>
        /// <param name="parentNode"></param>
        /// <param name="childrenNode"></param>
        private void UpdateTreeNode(TreeNode parentNode, TreeNode childrenNode)
        {
            //1.更新左侧视图
            if (parentNode != null)
            {
                parentNode.Nodes.Add(childrenNode);
                parentNode.Expand();
            }
            else
                map_treeView.Nodes.Add(childrenNode);
            //2.应对picture更新
            map_pictureBox.Image = _imageDic[childrenNode.Text].BMP;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="msg"></param>
        private void UpdateMapListBox(string msg)
        {
            map_listBox.Items.Add(msg);
            map_listBox.SelectedIndex = map_listBox.Items.Count - 1;
        }
        /// <summary>
        /// 绘制图像
        /// </summary>
        /// <param name="bmp"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="value"></param>
        private void PaintPoint(Bitmap bmp, int x, int y, byte value)
        {
            Graphics g = Graphics.FromImage(bmp);
            Color c = Color.FromArgb(value, value, value);
            Pen p = new Pen(c);
            SolidBrush brush = new SolidBrush(c);
            g.FillRectangle(brush, new Rectangle(x, y, 1, 1));
            //g.DrawLine(Pens.Black, new Point(x, y), new Point(bmp.Width, bmp.Height));
            map_pictureBox.Image = bmp;
        }

        private void PaintBitmap(Bitmap bmp, string nodeName)
        {
            _imageDic[nodeName].BMP = bmp;
            map_pictureBox.Image = bmp;
        }

        /// <summary>
        /// 更新listbox区域显示内容
        /// </summary>
        /// <param name="msg"></param>
        private delegate void UpdateMapListBoxHandler(string msg);
        /// <summary>
        /// 更新树视图委托
        /// </summary>
        /// <param name="parentNode"></param>
        /// <param name="childrenNode"></param>
        private delegate void UpdateTreeNodeHandler(TreeNode parentNode, TreeNode childrenNode);
        /// <summary>
        ///  保存Json委托
        /// </summary>
        /// <param name="jsonText"></param>
        private delegate void SaveJsonHandler(string jsonText);
        /// <summary>
        ///  保存位图委托
        /// </summary>
        /// <param name="bmp"></param>
        private delegate void SaveBitmapHandler(Bitmap bmp);
        /// <summary>
        ///  保存Excel委托
        /// </summary>
        /// <param name="dt"></param>
        private delegate void SaveExcelHandler(DataTable dt);
        /// <summary>
        /// 更新底部信息栏提示内容委托
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="statue"></param>
        private delegate void UpdateStatusLabelHandler(string msg, STATUE_ENUM statue = STATUE_ENUM.NORMAL);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="bmp"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="value"></param>
        private delegate void PaintPointHandler(Bitmap bmp, int x, int y, byte value);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="bmp"></param>
        /// <param name="nodeName"></param>
        private delegate void PaintBitmapHandler(Bitmap bmp, string nodeName);

        #endregion

        #region 主功能

        /// <summary>
        /// 读取图片
        /// </summary>
        private void ReadImage()
        {
            #region OpenFileDialog设置
            OpenFileDialog openfiledialog = new OpenFileDialog
            {
                Multiselect = false,
                RestoreDirectory = true,
                Filter = "图像文件|*.img;*.tif;*.bmp;*.jpg;*.png|矢量文件|*.shp"
            };
            #endregion
            if (openfiledialog.ShowDialog() == DialogResult.OK)
            {
                string fileName = Path.GetFileNameWithoutExtension(openfiledialog.FileName);
                string extension = Path.GetExtension(openfiledialog.FileName);
                if (extension == ".tif" || extension == ".img" || extension == ".bmp" || extension == ".jpg" || extension == ".png")
                {
                    //1.判断图像是否已加载
                    if (map_treeView.Nodes.OfType<TreeNode>().FirstOrDefault(p => p.Tag.Equals(fileName)) != null)
                    {
                        UpdateStatusLabel("图像不能重复添加", STATUE_ENUM.WARNING);
                        return;
                    }
                    //2.构建TreeNode用于存储数据和结点
                    TreeNode node = new TreeNode(fileName)
                    {
                        Tag = fileName
                    };
                    map_treeView.Nodes.Add(node);
                    _imageDic.Add(fileName, null);
                    //3.分波段读取图像并加载，开辟新的线程分波段读取数据
                    ThreadStart s = delegate { ReadBand(openfiledialog.FileName, node); };
                    Thread t = new Thread(s)
                    {
                        IsBackground = true
                    };
                    t.Start();
                }
                else if (extension == "shp")
                {
                    //IShpReader pShpReader = new ShpReader(fileName);
                    //后台读取shp并绘制到bmp
                }

            }
        }
        /// <summary>
        /// 波段读取
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="parentNode"></param>
        private void ReadBand(string rasterFilename, TreeNode parentNode)
        {
            string name = parentNode.Text;
            GRasterLayer _layer = new GRasterLayer(rasterFilename);
            _rasterDic.Add(_layer.Name, _layer);
            for (int i = 0; i < _layer.BandCollection.Count; i++)
            {
                IGBand band = _layer.BandCollection[i];
                band.BandName = name + "_波段_" + i;
                Bitmap2 bmp2 = new Bitmap2(bmp: band.GetBitmap(), name: band.BandName, gdalBand: band, gdalLayer: _layer);
                //获取band对应的bitmap格式图像，载入treedNode中
                _imageDic.Add(band.BandName, bmp2);
                TreeNode childrenNode = new TreeNode(band.BandName);
                Invoke(new UpdateTreeNodeHandler(UpdateTreeNode), parentNode, childrenNode);
            }
        }

        #endregion

        #region UI事件相应方法

        /// <summary>
        /// 底图区域功能按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Map_function_Click(object sender, EventArgs e)
        {
            ToolStripItem item = sender as ToolStripItem;
            switch (item.Name)
            {
                case "Export_Bitmap_ToolStripMenuItem":
                    Bitmap saveBmp = map_pictureBox.Image as Bitmap;
                    SaveBitmap(saveBmp);
                    //导出图片
                    break;
                case "DL_CLASS_toolStripButton":
                    if (map_treeView.SelectedNode == null)
                    {
                        UpdateStatusLabel("请选择一副图像或者一个图层后，进行分类操作", STATUE_ENUM.ERROR);
                        return;
                    }
                    else
                    {
                        DLClassifyForm dlclassify = new DLClassifyForm();
                        if (dlclassify.ShowDialog() == DialogResult.OK)
                        {
                            //1.选择处理那副图像
                            string imageName = map_treeView.SelectedNode.Text;
                            Bitmap2 imageBitmap2 = _imageDic[imageName];
                            GRasterLayer rasterLayer = imageBitmap2.GdalLayer;
                            ThreadStart clsfy_ts = delegate { RunClassify(rasterLayer, dlclassify.UseSLIC, dlclassify.PBName, dlclassify.CenterName, dlclassify.LabelName); };
                            Thread clsfy_t = new Thread(clsfy_ts);
                            clsfy_t.IsBackground = true;
                            clsfy_t.Start();
                        }
                    }
                    break;
                case "open_toolstripmenuitem"://添加图像
                    ReadImage();
                    break;
                case "open_contextMenuStrip":
                    ReadImage();
                    break;
                //超像素分割
                case "SLIC_toolStripButton":
                case "SLIC_toolStripMenu":
                    Bitmap bmp = map_pictureBox.Image as Bitmap;
                    if (bmp != null)
                    {
                        ThreadStart slic_ts = delegate { RunSLIC(bmp); };
                        Thread slic_t = new Thread(slic_ts);
                        slic_t.IsBackground = true;
                        slic_t.Start();
                    }
                    else
                    {
                        UpdateStatusLabel("未选中待计算图像，地图区域无图片", STATUE_ENUM.ERROR);
                    }
                    break;
                //超像素中心应用
                case "SLIC_Center_toolStripButton":
                case "SLIC_Center_toolStripMenu":
                    OpenFileDialog opg = new OpenFileDialog
                    {
                        Filter = "JSON文件|*.json"
                    };
                    if (opg.ShowDialog() == DialogResult.OK)
                    {
                        //1.读取center中心
                        using (StreamReader sr = new StreamReader(opg.FileName))
                        {
                            List<byte> colors = new List<byte>();
                            Center[] centers = SuperPixelSegment.ReadCenter(sr.ReadToEnd());
                            //2.设置使用图层
                            CenterApplyForm centerApplyForm = new CenterApplyForm();
                            if (centerApplyForm.ShowDialog() == DialogResult.OK)
                            {
                                ThreadStart s = delegate { RunCenter(centerApplyForm.FileNameCollection, centers); };
                                Thread t = new Thread(s);
                                t.IsBackground = true;
                                t.Start();
                            }
                        }
                    }
                    break;
                //强化学习模型训练入口 
                case "DQN_toolStripButton":
                    DQNForm dqnForm = new DQNForm();
                    dqnForm.RasterDic = _rasterDic;
                    if (dqnForm.ShowDialog() == DialogResult.OK)
                    {
                        string keyFeature = dqnForm.SelectedFeatureRasterLayer;
                        string keyLabel = dqnForm.SelectedLabelRasterLayer;
                        ThreadStart s = delegate { RunDQN(_rasterDic[keyFeature], _rasterDic[keyLabel]); };
                        Thread t = new Thread(s);
                        t.IsBackground = true;
                        t.Start();
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        ///  树视图点击捕捉，用于邮件弹出功能栏
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Map_treeView_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            Bitmap2 bmp2 = _imageDic[map_treeView.SelectedNode.Text];
            switch (item.Name)
            {
                case "bandCombine_ToolStripMenuItem":
                    BandForm bandModal = new BandForm
                    {
                        GdalLayer = bmp2.GdalLayer
                    };
                    if (bandModal.ShowDialog() == DialogResult.OK)
                    {
                        List<int> combineIndex = bandModal.BanCombineIndex;
                        Bitmap layerBitmap = GRGBCombine.Run(
                            bandModal.GdalLayer.BandCollection[combineIndex[0]].GetByteData(),
                            bandModal.GdalLayer.BandCollection[combineIndex[1]].GetByteData(),
                            bandModal.GdalLayer.BandCollection[combineIndex[2]].GetByteData());
                        Bitmap2 layerBitmap2 = new Bitmap2(bmp: layerBitmap, name: bandModal.GdalLayer.Name, gdalLayer: bandModal.GdalLayer);
                        //获取band对应的bitmap格式图像，载入treedNode中
                        _imageDic[bandModal.GdalLayer.Name] = layerBitmap2;
                        map_pictureBox.Image = layerBitmap2.BMP;
                    }
                    break;
                default:
                    break;
            }
        }
        /// <summary>
        /// 树视图结点操作
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Map_treeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            //左键，选择图像显示
            if (e.Button == MouseButtons.Left)
            {
                map_treeView.SelectedNode = e.Node;
                _selectBmp2 = _imageDic[e.Node.Text];
                map_pictureBox.Image = _selectBmp2?.BMP;
            }
            else if (e.Button == MouseButtons.Right)
            {
                map_treeView.SelectedNode = e.Node;
                tree_contextMenuStrip.Show(map_treeView, new Point(e.X, e.Y));
            }
        }

        #endregion

    }
}
