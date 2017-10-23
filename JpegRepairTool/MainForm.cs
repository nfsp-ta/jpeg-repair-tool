using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Collections;


namespace JpegRepairTool
{
    public partial class MainForm : Form
    {
        private byte[] imgBytes;
        private Color clr;
        private Point pnt;
        private int blocksize = 8;
        private MemoryStream ms = new MemoryStream();
        private int imgWidth = 0;
        private int imgHeight = 0;
        private int rows = 0;
        private int cols = 0;
        private Color[] clrBlocks;
        private int lastOffset = 0;

        private enum Edges
        {
            Top,
            Right,
            Left,
            Bottom
        }

        public MainForm()
        {
            InitializeComponent();
        }

        private void TestBtn_Click(object sender, EventArgs e)
        {
            Task.Run(() => Test2());
        }

        private void openBtn_Click(object sender, EventArgs e)
        {
            OpenFileDialog fileDlg = new OpenFileDialog();
            if (fileDlg.ShowDialog().ToString().Equals("OK"))
            {
                try
                {
                    imgBytes = File.ReadAllBytes(fileDlg.FileName);
                    LoadImage();
                }
                catch (Exception ee)
                {
                    Console.WriteLine(ee.Message);
                    Console.WriteLine(ee.StackTrace);
                }
            }
        }

        private void LoadImage()
        {
            ms = new MemoryStream(imgBytes);
            pictureBox1.Image = Image.FromStream(ms);
            pictureBox2.Image = Image.FromStream(ms);
            imgWidth = pictureBox1.Image.Width;
            imgHeight = pictureBox1.Image.Height;
            cols = imgWidth / blocksize;
            rows = imgHeight / blocksize;
            clrBlocks = new Color[rows * cols];
            using (Bitmap bmp = (Bitmap)Bitmap.FromStream(ms))
            {
                for (int row = 0; row < rows; row++)
                {
                    for (int col = 0; col < cols; col++)
                    {
                        int block = col + row * cols;
                        clrBlocks[block] = GetBlockColor(bmp, block);
                    }
                }
            }
            dataGridView1.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            dataGridView1.ColumnCount = 1 + rows;
        }

        /// <summary>
        /// Return the average color of any "block". Currently not used, may be used in future to correct an image based off of a thumbnail
        /// </summary>
        /// <param name="bmp">The Bitmap to test</param>
        /// <param name="block">The "block number" to test</param>
        /// <returns>Average Color of the entire block</returns>
        private Color GetBlockColor(Bitmap bmp, int block)
        {
            Point pnt = BlockToPoint(block);
            int x = pnt.X;
            int y = pnt.Y;
            int[] reds = new int[blocksize * blocksize];
            int[] blues = new int[blocksize * blocksize];
            int[] greens = new int[blocksize * blocksize];
            for (int i = 0; i < blocksize; i++)
            {
                for (int ii = 0; ii < blocksize; ii++)
                {
                    Color clr = bmp.GetPixel(x + i, y + ii);
                    reds[i * blocksize + ii] = clr.R;
                    blues[i * blocksize + ii] = clr.B;
                    greens[i * blocksize + ii] = clr.G;
                }
            }
            return Color.FromArgb((int)reds.Average(), (int)greens.Average(), (int)blues.Average());
        }

        /// <summary>
        /// Primary loop for image repair.
        /// </summary>
        private void Test2()
        {
            int len = imgBytes.Length;
            int optimumOffset = Convert.ToInt32(OffsetTxtBox.Text);
            int prevOffset = 0;
            int prevFE = 0;
            int skip = 0;
            int maxBlock = PointToBlock(imgWidth, imgHeight);
            bool loop = true;
            do
            {
                //List<int> offsets = new List<int>();
                SortedDictionary<int, long> offsets = new SortedDictionary<int, long>();
                int i = 0;
                for (i = 2; i < len; i++)
                {
                    if (imgBytes[i - 2] == 0xff && imgBytes[i - 1] == 0xda)
                        break;
                }
                if (i < optimumOffset)
                    i = optimumOffset;

                int firstEdge = 0;
                long initEdginess = 0;
                long initBlockiness = 0;
                Bitmap bmp = null;
                using (MemoryStream TmpMS = new MemoryStream(imgBytes))
                {
                    using (bmp = (Bitmap)Bitmap.FromStream(TmpMS))
                    {
                        firstEdge = FindFirstEdge(bmp, 0, skip);
                        /*if (firstEdge == prevFE)
                        {
                            skip = 0;
                            i = 500;
                            continue;
                        }*/
                        prevFE = firstEdge;

                        initEdginess = TestBmpEdginess(bmp, firstEdge);
                        for (int ii = 0; ii < firstEdge + 1; ii++)
                        {
                            initBlockiness += testBlock(bmp, ii);
                        }
                        DrawRedBox(firstEdge);
                    }
                }
                if (firstEdge >= maxBlock)
                    break;

                for (; i < len; i++)
                {
                    if (offsets.Count > 100 || offsets.Count > 0 && i > offsets.Last().Key + 1000)
                        break;

                    try
                    {
                        List<byte> b1 = imgBytes.ToList();
                        b1.Insert(i, 0x0d);
                        using (MemoryStream tmpMS = new MemoryStream(b1.ToArray()))
                        {
                            using (bmp = new Bitmap(Image.FromStream(tmpMS)))
                            {
                                bool matches = true;
                                int diff = 0;
                                long blockiness = 0;
                                for (int ii = 0; ii < firstEdge + 1; ii++)
                                {
                                    blockiness += testBlock(bmp, ii);
                                    if (blockiness > initBlockiness)
                                    {
                                        break;
                                    }
                                    /*Color c1 = GetBlockColor(bmp, ii);
                                    diff += Math.Abs(c1.R - clrBlocks[ii].R) + Math.Abs(c1.G - clrBlocks[ii].G) + Math.Abs(c1.B - clrBlocks[ii].B);
                                    if(diff < 3)
                                    {
                                        break;
                                    }*/

                                }
                                if (blockiness < initBlockiness)
                                {
                                    offsets.Add(i, blockiness);
                                }
                                /*else
                                    offsets.Add(i);*/
                            }
                        }
                        b1 = null;
                    }
                    catch (Exception ee)
                    {
                        //pictureBox2.Image = null;
                    }
                }
                if (offsets.Count == 0)
                {
                    skip++;
                    continue;
                }
                //offsets.Reverse();
                long minBlockiness = (long)offsets.Average(o => o.Value);
                List<int> offsets2 = offsets.Where(o => o.Value < minBlockiness).OrderByDescending(o => o.Key).Select(o => o.Key).ToList();

                bool writeByte = false;
                foreach (int offset in offsets2)
                {
                    List<byte> b1 = imgBytes.ToList();
                    b1.Insert(offset, 0x0d);
                    using (MemoryStream tmpMS = new MemoryStream(b1.ToArray()))
                    {
                        using (bmp = new Bitmap(Image.FromStream(tmpMS)))
                        {
                            /*pictureBox2.Image = Image.FromStream(tmpMS);
                            this.Refresh();*/
                            long edginess = TestBmpEdginess(bmp, firstEdge);
                            //MessageBox.Show("Offset: " + offset.ToString() + "\nEdginess: " + edginess.ToString());
                            if (edginess != -1 && edginess < initEdginess)
                            {
                                optimumOffset = offset;
                                initEdginess = edginess;
                                writeByte = true;
                            }
                        }
                    }
                    b1 = null;
                }
                if (writeByte)
                {
                    List<byte> b2 = imgBytes.ToList();
                    b2.Insert(optimumOffset, 0x0d);
                    imgBytes = b2.ToArray();
                    b2 = null;
                    prevOffset = optimumOffset;
                    lastOffset = optimumOffset;
                    DispOffset(optimumOffset);
                    TestHorzEdges();
                }
                else
                    skip++;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            } while (loop);
            /*
            using (MemoryStream TmpMS = new MemoryStream(imgBytes))
            {
                using (Bitmap bmp = (Bitmap)Bitmap.FromStream(TmpMS))
                {
                    pictureBox2.Image = null;
                    pictureBox2.Image = bmp;
                    this.Refresh();
                }
            }
            */
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }

            this.Activate();
        }

        /// <summary>
        /// Older/Alternate loop for image repair.
        /// </summary>
        private void Test1()
        {
            int len = imgBytes.Length;
            int optimumOffset = Convert.ToInt32(OffsetTxtBox.Text);
            int prevOffset = 0;
            do
            {
                List<int> offsets = new List<int>();
                int i = optimumOffset;
                /*                if (i > 1000)
                                  i -= 500;*/

                int firstEdge = 0;
                long initEdginess = 0;

                Bitmap bmp = null;
                using (MemoryStream TmpMS = new MemoryStream(imgBytes))
                {
                    using (bmp = (Bitmap)Bitmap.FromStream(TmpMS))
                    {

                        firstEdge = FindFirstEdge(bmp);
                        initEdginess = TestBmpEdginess(bmp, firstEdge);

                        int block = 0;
                        for (int y = 0; y < bmp.Height - blocksize; y += blocksize)
                        {
                            for (int x = 0; x < bmp.Width; x += blocksize)
                            {
                                block++;
                                if (block == firstEdge)
                                {
                                    using (Graphics gr = Graphics.FromImage(bmp))
                                    {
                                        using (Pen pen = new Pen(Color.Red, 1))
                                        {
                                            gr.DrawLine(pen, x + blocksize, y, x + blocksize, y + blocksize);
                                        }
                                    }
                                    pictureBox2.Image = null;
                                    pictureBox2.Image = bmp;
                                    this.Refresh();
                                }
                            }
                        }
                    }
                }

                for (; i < len; i++)
                {
                    try
                    {
                        List<byte> b1 = imgBytes.ToList();
                        b1.Insert(i, 0x0d);
                        using (MemoryStream tmpMS = new MemoryStream(b1.ToArray()))
                        {
                            using (bmp = new Bitmap(Image.FromStream(tmpMS)))
                            {
                                int edge = FindFirstEdge(bmp, firstEdge);
                                if (edge > firstEdge)
                                {
                                    offsets.Add(i);
                                }
                                /*else
                                    offsets.Add(i);*/
                            }
                        }
                        b1 = null;
                    }
                    catch (Exception ee)
                    {
                        pictureBox2.Image = null;
                    }
                }
                if (offsets.Count == 0)
                    break;
                offsets.Reverse();
                foreach (int offset in offsets)
                {
                    List<byte> b1 = imgBytes.ToList();
                    b1.Insert(offset, 0x0d);
                    using (MemoryStream tmpMS = new MemoryStream(b1.ToArray()))
                    {
                        using (bmp = new Bitmap(Image.FromStream(tmpMS)))
                        {
                            /*pictureBox2.Image = Image.FromStream(tmpMS);
                            this.Refresh();*/
                            long edginess = TestBmpEdginess(bmp, firstEdge);
                            //MessageBox.Show("Offset: " + offset.ToString() + "\nEdginess: " + edginess.ToString());
                            if (edginess != -1 && edginess < initEdginess)
                            {
                                optimumOffset = offset;
                                initEdginess = edginess;
                                /**/
                            }
                        }
                    }
                    b1 = null;
                }
                //OffsetTxtBox.Text = optimumOffset.ToString();
                List<byte> b2 = imgBytes.ToList();
                b2.Insert(optimumOffset, 0x0d);
                imgBytes = b2.ToArray();
                b2 = null;
                //ms = new MemoryStream(imgBytes);
                //pictureBox1.Image = Image.FromStream(ms);
                listView1.Items.Add(optimumOffset.ToString());
                prevOffset = optimumOffset;
                //this.Refresh();
                //pictureBox1.Image = null;
                //MessageBox.Show(optimumOffset.ToString());
                GC.Collect();
                GC.WaitForPendingFinalizers();
            } while (true);

            using (MemoryStream TmpMS = new MemoryStream(imgBytes))
            {
                using (Bitmap bmp = (Bitmap)Bitmap.FromStream(TmpMS))
                {
                    pictureBox2.Image = null;
                    pictureBox2.Image = bmp;
                    this.Refresh();
                }
            }

            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }

            this.Activate();
        }

        /// <summary>
        /// Experimental function to determine the "Edginess" of each row. Currently only displays the output in the "Grid" tab
        /// </summary>
        private void TestHorzEdges()
        {
            string msg = "Offset: " + lastOffset.ToString() + "\n";
            DataGridViewRow gridRow = new DataGridViewRow();
            gridRow.Cells.Add(new DataGridViewTextBoxCell() { Value = lastOffset });
            using (MemoryStream tmpMs = new MemoryStream(imgBytes))
            {
                using (Bitmap bmp = new Bitmap(tmpMs))
                {
                    float[] lines = new float[7];
                    for (int y = blocksize; y < bmp.Height - 1; y += blocksize)
                    {
                        float edginess = 0;
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            Color c1 = bmp.GetPixel(x, y - 2);
                            Color c2 = bmp.GetPixel(x, y - 1);
                            Color c3 = bmp.GetPixel(x, y);
                            Color c4 = bmp.GetPixel(x, y + 1);
                            float rdiff1 = Math.Abs(c1.R - c2.R);
                            float rdiff2 = Math.Abs(c2.R - c3.R);
                            float rdiff3 = Math.Abs(c3.R - c4.R);
                            float gdiff1 = Math.Abs(c1.G - c2.G);
                            float gdiff2 = Math.Abs(c2.G - c3.G);
                            float gdiff3 = Math.Abs(c3.G - c4.G);
                            float bdiff1 = Math.Abs(c1.B - c2.B);
                            float bdiff2 = Math.Abs(c2.B - c3.B);
                            float bdiff3 = Math.Abs(c3.B - c4.B);
                            edginess += ((2 * (imgWidth - x)) * Math.Abs(rdiff2 - ((rdiff1 + rdiff3) / 2)));
                            edginess += ((2 * (imgWidth - x)) * Math.Abs(gdiff2 - ((gdiff1 + gdiff3) / 2)));
                            edginess += ((2 * (imgWidth - x)) * Math.Abs(bdiff2 - ((bdiff1 + bdiff3) / 2)));
                        }
                        gridRow.Cells.Add(new DataGridViewTextBoxCell() { Value = edginess });
                        msg += ("Line: " + y.ToString() + " Edginess: " + edginess.ToString() + "\n");
                    }
                }
            }
            AddGridRow(gridRow);
        }

        /// <summary>
        /// Used to determine the overall "Edginess" of the bitmap, testing only up to a given block if provided.
        /// </summary>
        /// <param name="bmp">The Bitmap to test</param>
        /// <param name="bloksToTest">Optional. Maximum number of image "blocks" to test. 0 = test all.</param>
        /// <returns>arbitrary value where a lower value indicates "less edgy" which is desirable</returns>
        private long TestBmpEdginess(Bitmap bmp, long bloksToTest = 0)
        {
            long overallEdginess = 0;
            int[] diffs = new int[blocksize - 1];
            int[] vdiffs = new int[blocksize - 1];
            int topedge = 0;
            int block = -1;
            for (int y = 0; y < bmp.Height - blocksize; y += blocksize)
            {
                /*if (y == 80)
                    MessageBox.Show("!");*/

                for (int x = 1; x < bmp.Width - 1; x++)
                {
                    int diff = 0;
                    for (int i = 0; i < blocksize; i++)
                    {
                        Color c1 = bmp.GetPixel(x, y + i);
                        Color c2 = bmp.GetPixel(x - 1, y + i);
                        diff += (Math.Abs((int)c1.R - (int)c2.R) + Math.Abs((int)c1.G - (int)c2.G) + Math.Abs((int)c1.B - (int)c2.B));
                        if (y > 0)
                        {
                            Color c3 = bmp.GetPixel(x - 1, (y + i) - 1);
                            int ve = (Math.Abs((int)c2.R - (int)c3.R) + Math.Abs((int)c2.G - (int)c3.G) + Math.Abs((int)c2.B - (int)c3.B));
                            if (i == 0)
                                topedge += ve;
                            else
                                vdiffs[i - 1] += ve;
                        }
                    }
                    if (x % blocksize == 0)
                    {
                        if (y > 0)
                        {
                            overallEdginess += (topedge - (int)vdiffs.Average());
                            Array.Clear(vdiffs, 0, vdiffs.Length);
                            topedge = 0;
                        }
                        overallEdginess += (int)Math.Abs(diff - diffs.Average());
                        if (block == bloksToTest)
                            return overallEdginess;
                        block++;
                    }
                    else
                    {
                        int idx = (x % blocksize) - 1;
                        diffs[idx] = diff;
                    }
                }
            }
            return overallEdginess;

            /*
            long[] diffs = new long[bmp.Height];
            for (int y = 0; y < bmp.Height - 1; y++)
            {
                long diff = 0;
                for (int x = 0; x < bmp.Width; x++)
                {
                    Color c1 = bmp.GetPixel(x, y);
                    Color c2 = bmp.GetPixel(x, y + 1);
                    diff += Math.Abs(c1.R - c2.R);
                    diff += Math.Abs(c1.G - c2.G);
                    diff += Math.Abs(c1.B - c2.B);
                }
                diffs[y] = diff;
            }
            return diffs.Sum();*/
        }

        /// <summary>
        /// Find the first "block" which is considered "edgy" enough to be in error. 
        /// </summary>
        /// <param name="bmp">The Bitmap to test</param>
        /// <param name="max">The maximum number of blocks to test (default, 0 = test all)</param>
        /// <param name="skip">The number of blocks from the begining of the image to skip (default, 0 = skip none)</param>
        /// <returns>Number of the "block" considered "edgy" enough to be in error </returns>
        private int FindFirstEdge(Bitmap bmp, int max = 0, int skip = 0)
        {
            return ffe3(bmp, max, skip);
        }

        /// <summary>
        /// Third implementation of "FindFirstEdge" function
        /// </summary>
        /// <param name="bmp"></param>
        /// <param name="max"></param>
        /// <param name="skip"></param>
        /// <returns></returns>
        private int ffe3(Bitmap bmp, int max = 0, int skip = 0)
        {
            int i = 0;
            if (max == 0)
                max = (int)(imgWidth / blocksize) * (int)(imgHeight / blocksize);

            for (; i < 1 + max; i++)
            {
                if (testBlock(bmp, i) > 9)
                {
                    if (skip == 0)
                        return i;
                    else
                        skip--;
                }
            }
            return i;
        }

        /// <summary>
        /// Test the "edginess" / "blockiness" of an image "block"
        /// </summary>
        /// <param name="bmp">The Bitmap to test</param>
        /// <param name="block">The "block number" to test</param>
        /// <returns>arbitrary number indicating the "edginess" / "blockiness" of the indicated block. Lower is less "edgy" / "blocky" thus better.</returns>
        private int testBlock(Bitmap bmp, int block)
        {
            Point pnt = BlockToPoint(block);
            int blockiness = 0;
            int edgeCount = 0;
            int x = pnt.X;
            int y = pnt.Y;

            //MessageBox.Show(x + ", " + y);
            bool hasTop = y > 0;
            bool hasBottom = y + blocksize < bmp.Height;
            bool hasLeft = x > 0;
            bool hasRight = x + blocksize < bmp.Width;

            if (hasTop && x + blocksize <= imgWidth)
            {
                edgeCount++;
                blockiness += (int)TestEdge(bmp, block, Edges.Top);
            }
            if (hasRight && y + blocksize <= imgHeight)
            {
                edgeCount++;
                blockiness += (int)TestEdge(bmp, block, Edges.Right);
            }
            /*if (hasBottom && x + blocksize <= imgWidth)
            {
                edgeCount++;
                blockiness += (int)TestEdge(bmp, block, Edges.Bottom);
            }*/
            if (hasLeft && y + blocksize <= imgHeight)
            {
                edgeCount++;
                blockiness += (int)TestEdge(bmp, block, Edges.Left);
            }
            return blockiness / edgeCount;
        }

        /// <summary>
        /// Second implementation of "FindFirstEdge" function (Kept for reference and testing)
        /// </summary>
        /// <param name="bmp"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        private int ffe2(Bitmap bmp, int max = 0)
        {
            int block = 1;
            float[] diffs = new float[blocksize - 1];
            float[] vdiffs = new float[blocksize - 1];
            float topedge = 0;
            float hEdginess = 0;
            for (int y = 0; y < bmp.Height - blocksize; y += blocksize)
            {
                for (int x = blocksize; x < bmp.Width - blocksize; x += blocksize)
                {
                    block++;
                    float edginess = 0;
                    for (int i = 0; i < blocksize; i++)
                    {
                        Color c1 = bmp.GetPixel(x - 2, y + i);
                        Color c2 = bmp.GetPixel(x - 1, y + i);
                        Color c3 = bmp.GetPixel(x, y + i);
                        Color c4 = bmp.GetPixel(x + 1, y + i);
                        float diff1 = Math.Abs(c1.GetBrightness() - c2.GetBrightness());
                        float diff2 = Math.Abs(c2.GetBrightness() - c3.GetBrightness());
                        float diff3 = Math.Abs(c3.GetBrightness() - c4.GetBrightness());
                        edginess += Math.Abs(diff2 - ((diff1 + diff3) / 2));
                    }
                    if (edginess > 1)
                    {
                        return block;
                    }
                }
            }

            return block;
        }

        /// <summary>
        /// First implementation of "FindFirstEdge" function (Kept for reference and testing)
        /// </summary>
        /// <param name="bmp"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        private int ffe1(Bitmap bmp, int max = 0)
        {
            int block = 0;
            int[] diffs = new int[blocksize - 1];
            int[] vdiffs = new int[blocksize - 1];
            int topedge = 0;
            int hEdginess = 0;
            for (int y = 0; y < bmp.Height - blocksize; y += blocksize)
            {
                for (int x = 1; x < bmp.Width - 1; x++)
                {
                    int diff = 0;
                    for (int i = 0; i < blocksize; i++)
                    {
                        Color c1 = bmp.GetPixel(x, y + i);
                        Color c2 = bmp.GetPixel(x - 1, y + i);
                        diff += (Math.Abs((int)c1.R - (int)c2.R) + Math.Abs((int)c1.G - (int)c2.G) + Math.Abs((int)c1.B - (int)c2.B));
                        if (y > 0)
                        {
                            Color c3 = bmp.GetPixel(x - 1, (y + i) - 1);
                            int ve = (Math.Abs((int)c2.R - (int)c3.R) + Math.Abs((int)c2.G - (int)c3.G) + Math.Abs((int)c2.B - (int)c3.B));
                            if (i == 0)
                                topedge += ve;
                            else
                                vdiffs[i - 1] += ve;
                        }
                    }
                    if (x % blocksize == 0)
                    {
                        block++;
                        if (y > 0)
                        {
                            hEdginess = Math.Abs(topedge - (int)vdiffs.Average());
                            Array.Clear(vdiffs, 0, vdiffs.Length);
                            topedge = 0;
                        }
                        int edginess = Math.Abs(diff - (int)diffs.Average());
                        if (edginess > 560) //500
                        //if(edginess + hEdginess > 800)
                        {
                            /*if (max == 0)
                                MessageBox.Show("Edginess: " + edginess.ToString() + "\nH-Edginess: " + hEdginess.ToString());*/
                            return block;
                        }
                    }
                    else
                    {
                        int idx = (x % blocksize) - 1;
                        diffs[idx] = diff;
                    }
                }
            }

            return block;
            //return 0;
        }

        /// <summary>
        /// Tests the specific edge of a block for "edginess"
        /// </summary>
        /// <param name="bmp">The Bitmap to test</param>
        /// <param name="block">The "block" to test</param>
        /// <param name="e">The edge of the block to test</param>
        /// <returns>arbitrary number indicating the "edginess" of the indicated block and edge. Lower is less "edgy" thus better.</returns>
        private float TestEdge(Bitmap bmp, int block, Edges e)
        {
            Point pnt = BlockToPoint(block);
            int x = pnt.X;
            int y = pnt.Y;
            float[] edges = new float[blocksize * 3];
            for (int i = 0; i < blocksize; i++)
            {
                Color c1, c2, c3, c4;
                c1 = c2 = c3 = c4 = new Color();
                switch (e)
                {
                    case Edges.Bottom:
                        c1 = bmp.GetPixel(x + i, blocksize + y - 2);
                        c2 = bmp.GetPixel(x + i, blocksize + y - 1);
                        c3 = bmp.GetPixel(x + i, blocksize + y);
                        c4 = bmp.GetPixel(x + i, blocksize + y + 1);
                        break;
                    case Edges.Top:
                        c1 = bmp.GetPixel(x + i, y - 2);
                        c2 = bmp.GetPixel(x + i, y - 1);
                        c3 = bmp.GetPixel(x + i, y);
                        c4 = bmp.GetPixel(x + i, y + 1);
                        break;
                    case Edges.Right:
                        c1 = bmp.GetPixel(blocksize + x - 2, y + i);
                        c2 = bmp.GetPixel(blocksize + x - 1, y + i);
                        c3 = bmp.GetPixel(blocksize + x, y + i);
                        c4 = bmp.GetPixel(blocksize + x + 1, y + i);
                        break;
                    case Edges.Left:
                        c1 = bmp.GetPixel(x - 2, y + i);
                        c2 = bmp.GetPixel(x - 1, y + i);
                        c3 = bmp.GetPixel(x, y + i);
                        c4 = bmp.GetPixel(x + 1, y + i);
                        break;
                }
                int rdiff1 = Math.Abs(c1.R - c2.R);
                int rdiff2 = Math.Abs(c2.R - c3.R);
                int rdiff3 = Math.Abs(c3.R - c4.R);
                edges[i] = (rdiff2 - ((rdiff1 + rdiff3) / 2));
                int gdiff1 = Math.Abs(c1.G - c2.G);
                int gdiff2 = Math.Abs(c2.G - c3.G);
                int gdiff3 = Math.Abs(c3.G - c4.G);
                edges[i + blocksize] = (gdiff2 - ((gdiff1 + gdiff3) / 2));
                int bdiff1 = Math.Abs(c1.B - c2.B);
                int bdiff2 = Math.Abs(c2.B - c3.B);
                int bdiff3 = Math.Abs(c3.B - c4.B);
                edges[i + (2 * blocksize)] = (bdiff2 - ((bdiff1 + bdiff3) / 2));
            }
            if (edges.Count(v => v > 9) > 14) //14 | all > abs(75) (16)
            {
                var tst = edges.Where(v => v > 9).Average();
                return tst;
            }
            if (edges.Count() == edges.Count(v => v > 1.9))
            {
                var tst = Math.Min(edges.Average() * 10, 100);
                return tst;
            }
            if (edges.Count() == edges.Count(v => Math.Abs(v) > 16))
            {
                var tst = edges.Select(v => Math.Abs(v)).Average();
                return tst;
            }
            return 0;
        }

        #region AsyncFormViewUpdates

        delegate void DrawRedBoxCallback(int block);

        private void DrawRedBox(int block)
        {
            if (this.pictureBox2.InvokeRequired)
            {
                DrawRedBoxCallback c = new DrawRedBoxCallback(DrawRedBox);
                this.Invoke(c, new object[] { block });
            }
            else
            {
                using (MemoryStream TmpMS = new MemoryStream(imgBytes))
                {
                    using (Bitmap bmp = (Bitmap)Bitmap.FromStream(TmpMS))
                    {
                        Point pnt = BlockToPoint(block);
                        int x = pnt.X;
                        int y = pnt.Y;
                        int w = blocksize;
                        int h = blocksize;
                        if (x + w > imgWidth)
                            w = imgWidth - x;
                        if (y + h > imgHeight)
                            h = imgHeight - y;
                        using (Graphics gr = Graphics.FromImage(bmp))
                        {
                            using (Pen pen = new Pen(Color.Red, 1))
                            {

                                gr.DrawRectangle(pen, x, y, w, h);
                            }
                        }
                        ms = new MemoryStream();
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                    }
                }
                pictureBox2.Image = null;
                pictureBox2.Image = Image.FromStream(ms);
                this.Refresh();
            }
        }

        delegate void DispOffsetCallback(int offset);

        private void DispOffset(int offset)
        {
            if (this.pictureBox2.InvokeRequired)
            {
                DispOffsetCallback c = new DispOffsetCallback(DispOffset);
                this.Invoke(c, new object[] { offset });
            }
            else
            {
                listView1.Items.Add(offset.ToString());
                this.Refresh();
            }
        }

        delegate void AddGridRowCallback(DataGridViewRow gridRow);

        private void AddGridRow(DataGridViewRow gridRow)
        {
            if (this.dataGridView1.InvokeRequired)
            {
                AddGridRowCallback c = new AddGridRowCallback(AddGridRow);
                this.Invoke(c, new object[] { gridRow });
            }
            else
            {
                dataGridView1.Rows.Add(gridRow);
                this.Refresh();
            }
        }

        #endregion

        #region HelperFunctions

        /// <summary>
        ///  Converts a Point to the corresponding "block" number that contains that point.
        /// </summary>
        /// <param name="pnt">A System.Drawing.Point struct</param>
        /// <returns>"Block" number as an int</returns>
        private int PointToBlock(Point pnt)
        {
            return PointToBlock(pnt.X, pnt.Y);
        }

        /// <summary>
        ///  Converts x, y coordinates to the corresponding "block" number that contains that point.
        /// </summary>
        /// <param name="x">x coordinate</param>
        /// <param name="y">y coordinate</param>
        /// <returns>"Block" number as an int</returns>
        private int PointToBlock(int x, int y)
        {
            int col = x / blocksize;
            int row = y / blocksize;
            return col + (row * cols);
        }

        /// <summary>
        ///  Returns the x, y coordinates at the top-left corner of a "block" as a System.Drawing.Point struct
        /// </summary>
        /// <param name="block">"block" number</param>
        /// <returns>System.Drawing.Point struct represending the top-left corner of the "block"</returns>
        private Point BlockToPoint(int block)
        {
            int col = (block % cols);
            int row = (block - col) / cols;
            int x = col * blocksize;
            int y = row * blocksize;
            return new Point(x, y);
        }

        #endregion

    }
}
