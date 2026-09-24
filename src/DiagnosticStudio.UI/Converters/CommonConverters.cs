using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiagnosticStudio.UI.Converters
{
    /// <summary>
    /// 索引转 Visibility 转换器 (用于 Tab 页面切换)
    /// </summary>
    public sealed class IndexToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int currentIndex && parameter != null)
            {
                if (int.TryParse(parameter.ToString(), out int targetIndex))
                {
                    return currentIndex == targetIndex ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 索引与单选框 IsChecked 双向转换器
    /// </summary>
    public sealed class IndexToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int currentIndex && parameter != null)
            {
                if (int.TryParse(parameter.ToString(), out int targetIndex))
                {
                    return currentIndex == targetIndex;
                }
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isChecked && isChecked && parameter != null)
            {
                if (int.TryParse(parameter.ToString(), out int targetIndex))
                {
                    return targetIndex;
                }
            }
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// 反向布尔转 Visibility 转换器 (false -> Visible, true -> Collapsed)
    /// </summary>
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
            {
                return v != Visibility.Visible;
            }
            return false;
        }
    }
}
