using System;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash;
public interface ICalloutService {
    public CalloutTheme GetTheme();

    Window ShowCallout(string markDownText, Rect target, Window parentWindow, double width = 200, 
                     double angle = double.MinValue, CalloutTheme theme = CalloutTheme.Light, double fontSize = 15, double horizontalPercentOffset = 0);

    Window ShowCallout(string markDownText, FrameworkElement target, Window parentWindow, double width = 200,
                       double angle = double.MinValue, CalloutTheme theme = CalloutTheme.Light, double fontSize = 15, double horizontalPercentOffset = 0);
}