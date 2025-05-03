using System.Collections.Generic;
using UnityEngine;

public class SVGPathParser
{
    private int index = 0;
    private string pathData = "";
    private char currentCommand = 'M';
    private Vector2 currentPoint = Vector2.zero;
    private Vector2 subpathStart = Vector2.zero;

    public List<Vector2[]> ParsePath(string data)
    {
        pathData = data;
        index = 0;
        currentCommand = 'M';
        currentPoint = Vector2.zero;
        subpathStart = Vector2.zero;

        List<Vector2[]> segments = new List<Vector2[]>();
        List<Vector2> currentSegment = new List<Vector2>();

        while (index < pathData.Length)
        {
            SkipWhitespace();

            if (index >= pathData.Length)
                break;

            char c = pathData[index];

            if (IsCommand(c))
            {
                currentCommand = c;
                index++;
            }

            switch (currentCommand)
            {
                case 'M': // MoveTo absolute
                case 'm': // MoveTo relative
                    if (currentSegment.Count > 1)
                    {
                        segments.Add(currentSegment.ToArray());
                        currentSegment = new List<Vector2>();
                    }
                    MoveTo(currentCommand == 'm');
                    currentSegment.Add(currentPoint);
                    break;

                case 'L': // LineTo absolute
                case 'l': // LineTo relative
                    LineTo(currentCommand == 'l');
                    currentSegment.Add(currentPoint);
                    break;

                case 'H': // Horizontal line absolute
                case 'h': // Horizontal line relative
                    HorizontalLineTo(currentCommand == 'h');
                    currentSegment.Add(currentPoint);
                    break;

                case 'V': // Vertical line absolute
                case 'v': // Vertical line relative
                    VerticalLineTo(currentCommand == 'v');
                    currentSegment.Add(currentPoint);
                    break;

                case 'Z': // Close path
                case 'z':
                    if (currentSegment.Count > 0)
                    {
                        currentSegment.Add(subpathStart);
                        segments.Add(currentSegment.ToArray());
                        currentSegment = new List<Vector2>();
                    }
                    currentPoint = subpathStart;
                    index++;
                    break;

                case 'C': // Cubic bezier absolute
                case 'c': // Cubic bezier relative
                    CubicBezierTo(currentCommand == 'c', segments, ref currentSegment);
                    break;

                case 'Q': // Quadratic bezier absolute
                case 'q': // Quadratic bezier relative
                    QuadraticBezierTo(currentCommand == 'q', segments, ref currentSegment);
                    break;

                default:
                    index++;
                    break;
            }
        }

        // Add final segment
        if (currentSegment.Count > 1)
        {
            segments.Add(currentSegment.ToArray());
        }

        return segments;
    }

    private bool IsCommand(char c)
    {
        return "MmLlHhVvZzCcQq".IndexOf(c) != -1;
    }

    private void SkipWhitespace()
    {
        while (index < pathData.Length && (char.IsWhiteSpace(pathData[index]) || pathData[index] == ','))
        {
            index++;
        }
    }

    private float ParseFloat()
    {
        SkipWhitespace();

        string number = "";
        while (index < pathData.Length && (char.IsDigit(pathData[index]) || pathData[index] == '.' || pathData[index] == '-' || pathData[index] == '+'))
        {
            number += pathData[index];
            index++;
        }

        return float.Parse(number);
    }

    private void MoveTo(bool relative)
    {
        float x = ParseFloat();
        float y = ParseFloat();

        if (relative)
        {
            currentPoint += new Vector2(x, y);
        }
        else
        {
            currentPoint = new Vector2(x, y);
        }

        subpathStart = currentPoint;

        // After a MoveTo, subsequent coordinate pairs are treated as implicit LineTo commands
        currentCommand = relative ? 'l' : 'L';
    }

    private void LineTo(bool relative)
    {
        float x = ParseFloat();
        float y = ParseFloat();

        if (relative)
        {
            currentPoint += new Vector2(x, y);
        }
        else
        {
            currentPoint = new Vector2(x, y);
        }
    }

    private void HorizontalLineTo(bool relative)
    {
        float x = ParseFloat();

        if (relative)
        {
            currentPoint.x += x;
        }
        else
        {
            currentPoint.x = x;
        }
    }

    private void VerticalLineTo(bool relative)
    {
        float y = ParseFloat();

        if (relative)
        {
            currentPoint.y += y;
        }
        else
        {
            currentPoint.y = y;
        }
    }

    private void CubicBezierTo(bool relative, List<Vector2[]> segments, ref List<Vector2> currentSegment)
    {
        Vector2 control1 = ParsePoint(relative);
        Vector2 control2 = ParsePoint(relative);
        Vector2 endPoint = ParsePoint(relative);

        const int steps = 10; // Adjust for smoother curves
        Vector2 previousPoint = currentPoint;

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 point = Mathf.Pow(1 - t, 3) * previousPoint +
                            3 * Mathf.Pow(1 - t, 2) * t * control1 +
                            3 * (1 - t) * Mathf.Pow(t, 2) * control2 +
                            Mathf.Pow(t, 3) * endPoint;

            currentSegment.Add(point);
        }

        currentPoint = endPoint;
    }

    private void QuadraticBezierTo(bool relative, List<Vector2[]> segments, ref List<Vector2> currentSegment)
    {
        Vector2 control = ParsePoint(relative);
        Vector2 endPoint = ParsePoint(relative);

        const int steps = 10; // Adjust for smoother curves
        Vector2 previousPoint = currentPoint;

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 point = Mathf.Pow(1 - t, 2) * previousPoint +
                            2 * (1 - t) * t * control +
                            Mathf.Pow(t, 2) * endPoint;

            currentSegment.Add(point);
        }

        currentPoint = endPoint;
    }

    private Vector2 ParsePoint(bool relative)
    {
        float x = ParseFloat();
        float y = ParseFloat();

        if (relative)
        {
            return currentPoint + new Vector2(x, y);
        }
        else
        {
            return new Vector2(x, y);
        }
    }
}
