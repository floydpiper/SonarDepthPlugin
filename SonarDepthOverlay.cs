using GMap.NET;
using GMap.NET.WindowsForms;
using MissionPlanner.Controls;
using MissionPlanner.GCSViews;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Net;
using System.Windows.Forms;


namespace MissionPlanner.plugins
{
    internal class SonarDepthPlugin : MissionPlanner.Plugin.Plugin
    {
        // Set private fields and variables
        private Panel depthBar;
        private QuickView centerPanel;
        private QuickView sonarView;
        private TrackBar sonarSlider;
        private TextBox changeMaxDepth;
        private PointLatLng? lastPlotted = null;
        private Color color;
        private double simulatedDepth;
        private bool madeOriginalRouteWhite = false;
        private string lastSonarInput;
        private int depthCounter = 0;
        //Pen tickPen = new Pen(Color.Black, 1);
        //Font tickFont = new Font("Arial", 8);
        //Brush textBrush = new SolidBrush(Color.Black);
        List<(PointLatLng, PointLatLng, string, float)> plottedRoutes = new List<(PointLatLng, PointLatLng, string, float)>();

        public override string Name => "Sonar Depth Overlay";
        public override string Version => "1.0";
        public override string Author => "Piper Floyd";


        /// <summary>
        /// Inherited function from Plugins. Runs once at startup. Displays message box in the beginning. 
        /// </summary>
        public override bool Init()
        {
            CustomMessageBox.Show("Sonar Depth Plugin");
            loopratehz = 1f; // 1 Hz loop rate (Fastest allowed to run)
            return true;
        }


        /// <summary>
        /// Inherited function from Plugins. Runs once at startup. Used to create the GUI features.
        /// Adds sonar depth information to the "Quick" tab.
        /// </summary>
        public override bool Loaded()
        {
            var quickPanelObj = Host.MainForm.FlightData.Controls.Find("tableLayoutPanelQuick", true).FirstOrDefault();
            var quickPanel = quickPanelObj as TableLayoutPanel;

            if (quickPanel != null)
            {
                // Displays sonar depth in the Quick tab
                sonarView = new QuickView
                {
                    Name = "quickViewSonar",
                    desc = "Sonar Depth (m)",
                    number = 0.0,
                    numberformat = "0.00",
                    numberColor = Color.FromArgb(86, 197, 137),
                    Size = new Size(140, 80)
                };
                sonarView.Padding = new Padding(0, 5, 0, 0);

                // Create color gradient bar
                depthBar = new Panel
                {
                    Height = 15,
                    Dock = DockStyle.Fill,
                    BackColor = Color.White
                };

                // Paint color gradient and ticks onto the bar
                depthBar.Paint += DrawGradient;
                depthBar.Paint += DrawTickMarks;

                // Text box to allow user input for changing max sonar depth range
                changeMaxDepth = new TextBox
                {
                    TextAlign = HorizontalAlignment.Center,
                    Name = "changeMaxDepth",
                    Dock = DockStyle.Bottom,
                    Width = 150
                };
                centerPanel = new QuickView
                {
                    desc = "Adjust max depth (m)",
                    Dock = DockStyle.Top,
                    Padding = new Padding(10)
                };
                centerPanel.Controls.Add(changeMaxDepth);

                // Add sonar depth info to the tab
                refreshGUI();
            }
            return true;
        }


        /// <summary>
        /// Inherited function from Plugins. Runs at the loopratehz set in Init()
        /// </summary>
        public override bool Loop()
        {
            // Grabs the sonar depth from the rover and its associated color
            float sonarDepth = 3.0f;

            // ---------------------- CHANGE TO MAKE ROVER WORK ---------------------- //
            //                                                                         //
            // Uncomment below:                                                        //
            //float sonarDepth = grabSonarDepth();                                     //
            //                                                                         //
            // Will need to comment line 115: float sonarDepth = 3.0f;                 //
            //                                                                         //
            // ---------------------- END CHANGE ------------------------------------- //


            // Need for the GUI to run
            Host.FDGMapControl.BeginInvokeIfRequired(() =>
            {
                // Re-add sonar depth view as something is overriding it
                var quickPanel = Host.MainForm.FlightData.Controls.Find("tableLayoutPanelQuick", true).FirstOrDefault() as TableLayoutPanel;

                if (quickPanel != null && (sonarView?.Parent != quickPanel))
                {
                    refreshGUI();
                }

                if (sonarView != null)
                {
                    var sonarInput = changeMaxDepth.Text; // user input to change max sonar depth

                    // Updates the color of the depth value in the quick view tab
                    sonarView.number = sonarDepth;
                    sonarView.numberColor = grabColor((sonarDepth));

                    // Update the tick marks on the gradient based on user input and recolors breadcrumbs
                    if (sonarInput != lastSonarInput)
                    {
                        lastSonarInput = sonarInput;
                        int parsed;
                        bool result = int.TryParse(sonarInput, out parsed);

                        if (result)
                        {
                            var sonarNewValue = Convert.ToDouble(sonarInput);

                            // ---------------------- USED FOR TESTING ONLY -------------------------- //
                            //                                                                         //
                            //sonarView.number = sonarNewValue;                                        //
                            //sonarView.numberColor = grabColor((float)sonarNewValue);                 //
                            //                                                                         //
                            // ---------------------- END TESTING ------------------------------------ //

                            repaintBreadcrumbs(plottedRoutes);
                            depthBar.Refresh();
                        }
                        Host.FDGMapControl.Refresh();
                    }

                    plotBreadcrumbs();
                    Host.FDGMapControl.Refresh();
                }
            });

            return true;
        }


        /// <summary>
        /// Inherited function from Plugins to exit.
        /// </summary>
        public override bool Exit()
        {
            return true;
        }


        /// <summary>
        /// Re-adds the GUI elements to the Quick tab.
        /// </summary>
        public void refreshGUI()
        {
            var quickPanel = Host.MainForm.FlightData.Controls.Find("tableLayoutPanelQuick", true).FirstOrDefault() as TableLayoutPanel;

            quickPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
            quickPanel.Controls.Add(sonarView, 0, quickPanel.RowCount);
            quickPanel.Controls.Add(centerPanel, 0, quickPanel.RowCount);
            quickPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            quickPanel.Controls.Add(depthBar, 0, quickPanel.RowCount);
            quickPanel.SetColumnSpan(depthBar, quickPanel.ColumnCount);
            sonarView.Refresh();
        }


        /// <summary>
        /// Draws tick marks and number labels on the depth bar in the Quick tab
        /// </summary>
        private void DrawTickMarks(object sender, PaintEventArgs e)
        {
            var panel = sender as Panel;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            var sonarInput = changeMaxDepth.Text;

            // start as default value  of 10m
            if (sonarInput == "")
            {
                sonarInput = "10";
            }

            int totalTicks = 5;
            int iter = 0;
            bool result = int.TryParse(sonarInput, out iter);
            double maxDepth = 0;

            // If user changed max depth
            if (result == true)
            {
                int sonarNewValue = Convert.ToInt16(sonarInput);
                maxDepth = sonarNewValue;
            }

            // Define panel dimensions for the ticks
            int panelWidth = panel.Width - 5;
            int panelHeight = panel.Height;
            int margin = 0;
            int tickHeight = 10;
            float step = panelWidth / (float)totalTicks;

            // Draw ticks evenly spaced on the gradient 
            using (Pen tickPen = new Pen(Color.Black, 1))
            using (Font tickFont = new Font("Arial", 8))
            using (Brush textBrush = new SolidBrush(Color.Black))
            {
                for (int i = 0; i <= totalTicks; i++)
                {
                    float x = (float)Math.Round(i * step);
                    double tickIter = maxDepth / totalTicks;
                    double depthValue = i * tickIter;

                    // start as default value  of 10m
                    if (i == 0)
                    {
                        x = 5;
                    }

                    g.DrawLine(tickPen, x, margin, x, margin + tickHeight);
                    string label = depthValue.ToString();
                    SizeF textSize = g.MeasureString(label, tickFont);
                    g.DrawString(label, tickFont, textBrush, x - textSize.Width / 2, margin + tickHeight + 2);
                }
            }
        }


        /// <summary>
        /// Plots the breadcrumbs as the rover moves. The color represents an associated depth of the water. 
        /// </summary>
        public void plotBreadcrumbs()
        {
            // Grabs the sonar depth from the rover and its associated color
            float sonarDepth = 3.0f;

            // ---------------------- CHANGE TO MAKE ROVER WORK ---------------------- //
            //                                                                         //
            // Uncomment below:                                                        //
            //float sonarDepth = grabSonarDepth();                                     //
            //                                                                         //
            // Will need to comment line 115: float sonarDepth = 3.0f;                 //
            //                                                                         //
            // ---------------------- END CHANGE ------------------------------------- //

            Color color = grabColor(sonarDepth);

            // Grab position of rover
            var pos = MainV2.comPort.MAV.cs.Location;
            if (pos == null || pos.Lat == 0 || pos.Lng == 0)
                return;

            var overlay = Host.FDGMapControl.Overlays.FirstOrDefault(o => o.Id == "routes");

            // Make the original route white in order to see breadcrumbs better
            if (!madeOriginalRouteWhite && overlay.Routes.Count > 0)
            {
                foreach (var r in overlay.Routes)
                {
                    r.Stroke = new Pen(Color.White, 3);
                }
                madeOriginalRouteWhite = true;
            }

            // Create a small offset to simulate a dot
            PointLatLng current = new PointLatLng(pos.Lat, pos.Lng);
            PointLatLng offset = new PointLatLng(current.Lat + 0.0000005, current.Lng + 0.0000005);

            Pen sonarPen = new Pen(color, 12)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            // Create unique dot - I think GMAProutes require this
            string routeName = $"SonarDot_{DateTime.Now.Ticks}";
            var dotRoute = new GMapRoute(new List<PointLatLng> { current, offset }, routeName)
            {
                Stroke = sonarPen
            };

            overlay.Routes.Add(dotRoute);
            Host.FDGMapControl.Refresh();

            // Save plotted location of breadcumbs for later
            if (dotRoute != null)
            {
                float depth = grabSonarDepth();
                plottedRoutes.Add((current, offset, routeName, depth));
            }
        }


        /// <summary>
        /// Repaints the breadcrumbs to the new associated depth scale after the user enters a new max depth
        /// </summary>
        /// <param name="plottedRoutes">A list of the previously plotted breadcrumbs </param>
        public void repaintBreadcrumbs(List<(PointLatLng, PointLatLng, string, float)> plottedRoutes)
        {
            var overlay = Host.FDGMapControl.Overlays.FirstOrDefault(o => o.Id == "routes");
            // Only in repaintBreadcrumbs()


            for (int i = 0; i < plottedRoutes.Count; i++)
            {
                var route = plottedRoutes[i]; // Grabs tuple

                // Extract breadcrumb information 
                var current = route.Item1;
                var offset = route.Item2;
                var routeName = route.Item3;
                var depth = 3.0f;

                // ---------------------- CHANGE TO MAKE ROVER WORK ---------------------- //
                //                                                                         //
                // Uncomment below:                                                        //
                //var depth = route.Item4;                                                 //
                //                                                                         //
                // Will need to comment out line 376: var depth = 3.0f;                    //
                //                                                                         //
                // ---------------------- END CHANGE ------------------------------------- //

                var color = grabColor(depth);

                Pen sonarPen = new Pen(color, 12)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };

                var dotRoute = new GMapRoute(new List<PointLatLng> { current, offset }, routeName)
                {
                    Stroke = sonarPen
                };

                overlay.Routes.Add(dotRoute);

                Host.FDGMapControl.Refresh();
            }
        }


        /// <summary>
        /// Grabs the current sonar depth from the rover.
        /// </summary>
        /// <returns> The sonar depth as a float. </returns>
        public float grabSonarDepth()
        {
            try
            {
                // -------------------- CHANGE TO MAKE BLUE BOAT WORK -------------------- //
                //                                                                         //
                // Uncomment below:                                                        //
                //IPEndPoint RemoteIpEndPoint = new IPEndPoint(ipAddress, 0);              //
                //Byte[] receiveBytes = receivingUdpClient.Receive(ref RemoteIpEndPoint);  //
                //                                                                         //
                //string returnData = Encoding.ASCII.GetString(receiveBytes);              //
                //string[] parts = returnData.Split(',');                                  //
                //var depth = (float)Convert.ToDouble(parts[2]);                           //
                //                                                                         //
                //return (float)depth;                                                     //
                //                                                                         //
                // ---------------------- END CHANGE ------------------------------------- //

                return MainV2.comPort.MAV.cs.sonarrange;
            }
            catch
            {
                return -42; // Sonar not connected
            }
        }


        /// <summary>
        /// Paints a color gradient between two colors on the depth bar in the Quick tab
        /// </summary>
        private void DrawGradient(object sender, PaintEventArgs e)
        {
            var panel = sender as Panel;
            if (panel != null)
            {
                Rectangle rect = panel.ClientRectangle;

                int midX = rect.X + rect.Width / 2;

                // First half: LightSalmon -> Blue
                using (var brush1 = new LinearGradientBrush(
                    new Rectangle(rect.X, rect.Y, rect.Width / 2, rect.Height),
                    Color.FromArgb(255, 253, 255, 139),
                    Color.LightSalmon,
                    LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillRectangle(brush1, new Rectangle(rect.X, rect.Y, rect.Width / 2, rect.Height));
                }

                // Second half: Blue -> Purple
                using (var brush2 = new LinearGradientBrush(
                    new Rectangle(midX, rect.Y, rect.Width / 2, rect.Height),
                    Color.LightSalmon,
                    Color.Purple,
                    LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillRectangle(brush2, new Rectangle(midX, rect.Y, rect.Width / 2, rect.Height));
                }
            }
        }


        /// <summary>
        /// Grabs the associated color of the depth on the gradient.
        /// </summary>
        /// <param name="sonardepth">The current sonar depth from the rover.</param>
        /// <returns> The associated color. </returns>
        public Color grabColor(float sonardepth)
        {

            Color start = Color.FromArgb(255, 253, 255, 139);
            Color middle = Color.LightSalmon;
            Color end = Color.Purple;
            int r = 0;
            int g = 0;
            int b = 0;

            var sonarInput = changeMaxDepth.Text;
            int iter = 0;
            float maxDepth = 0;


            if (sonarInput == "")
            {
                sonarInput = "10"; // start as default value  of 10m
            }
            bool result = int.TryParse(sonarInput, out iter);

            if (result == true)
            {
                int sonarNewValue = Convert.ToInt16(sonarInput);
                maxDepth = sonarNewValue;
            }

            if (sonardepth < 0 || sonardepth > maxDepth)
            {
                return Color.FromArgb(r, g, b); // Breadcrumb set to black if out of bounds
            }

            //Color gradient interpolation
            //c = a + (b - a) * t
            float t = sonardepth / maxDepth;
            if (t < 0.5f)
            {
                // Interpolate between start and middle
                float localT = t / 0.5f; // map [0, 0.5] -> [0, 1]
                r = (int)(start.R + (middle.R - start.R) * localT);
                g = (int)(start.G + (middle.G - start.G) * localT);
                b = (int)(start.B + (middle.B - start.B) * localT);
            }
            else
            {
                // Interpolate between middle and end
                float localT = (t - 0.5f) / 0.5f; // map [0.5, 1] -> [0, 1]
                r = (int)(middle.R + (end.R - middle.R) * localT);
                g = (int)(middle.G + (end.G - middle.G) * localT);
                b = (int)(middle.B + (end.B - middle.B) * localT);
            }

            return Color.FromArgb(r, g, b);
        }
    }
}
