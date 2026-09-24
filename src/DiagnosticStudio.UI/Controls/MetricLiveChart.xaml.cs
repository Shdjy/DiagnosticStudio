using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DiagnosticStudio.UI.Controls
{
    /// <summary>
    /// 高性能轻量级自绘实时折线图
    /// </summary>
    public partial class MetricLiveChart : UserControl
    {
        private readonly List<double> _values = new List<double>();
        private const int MaxDataPoints = 60;

        public MetricLiveChart()
        {
            InitializeComponent();
        }

        public void SetTitle(string title)
        {
            TitleText.Text = title;
        }

        public void AddDataPoint(double value, string displayValue)
        {
            _values.Add(value);
            if (_values.Count > MaxDataPoints)
            {
                _values.RemoveAt(0);
            }

            CurrentValueText.Text = displayValue;
            Redraw();
        }

        public void Clear()
        {
            _values.Clear();
            CurrentValueText.Text = "--";
            ChartCanvas.Children.Clear();
        }

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Redraw();
        }

        private void Redraw()
        {
            ChartCanvas.Children.Clear();
            double w = ChartCanvas.ActualWidth;
            double h = ChartCanvas.ActualHeight;

            if (w < 20 || h < 20 || _values.Count < 2)
            {
                return;
            }

            // 绘制横向网格线
            var gridPen = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            for (int i = 1; i <= 3; i++)
            {
                double y = h * (i / 4.0);
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = w,
                    Y2 = y,
                    Stroke = gridPen,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                ChartCanvas.Children.Add(line);
            }

            double minVal = _values.Min();
            double maxVal = _values.Max();
            double range = maxVal - minVal;
            if (range < 1e-4)
            {
                range = 1.0;
            }

            // 构造折线几何
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            double stepX = w / (MaxDataPoints - 1);
            int startIndex = MaxDataPoints - _values.Count;

            var points = new PointCollection();
            for (int i = 0; i < _values.Count; i++)
            {
                double x = (startIndex + i) * stepX;
                double norm = (_values[i] - minVal) / range;
                double y = h - (norm * (h - 30) + 15);
                points.Add(new Point(x, y));
            }

            polyline.Points = points;
            ChartCanvas.Children.Add(polyline);

            // 在最新数据点上绘制发光小圆点
            if (points.Count > 0)
            {
                var lastPt = points.Last();
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5
                };
                Canvas.SetLeft(dot, lastPt.X - 4);
                Canvas.SetTop(dot, lastPt.Y - 4);
                ChartCanvas.Children.Add(dot);
            }
        }
    }
}
