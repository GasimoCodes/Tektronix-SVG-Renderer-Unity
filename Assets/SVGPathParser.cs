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
        currentPoint = Vector2.zero;
        subpathStart = Vector2.zero;
        currentCommand = 'M';

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

            bool relative = char.IsLower(currentCommand);

            switch (char.ToUpper(currentCommand))
            {
                case 'M': // MoveTo
                          // If we have points in the current segment, add it to segments
                    if (currentSegment.Count > 0)
                    {
                        segments.Add(currentSegment.ToArray());
                        currentSegment = new List<Vector2>();
                    }

                    currentPoint = ParsePoint(relative);
                    subpathStart = currentPoint;
                    currentSegment.Add(currentPoint);

                    // After a MoveTo command, the command automatically changes to LineTo
                    currentCommand = relative ? 'l' : 'L';
                    break;

                case 'L': // LineTo
                    currentPoint = ParsePoint(relative);
                    currentSegment.Add(currentPoint);
                    break;

                case 'H': // Horizontal LineTo
                    float x = ParseFloat();
                    if (relative)
                        currentPoint.x += x;
                    else
                        currentPoint.x = x;

                    currentSegment.Add(currentPoint);
                    break;

                case 'V': // Vertical LineTo
                    float y = ParseFloat();
                    if (relative)
                        currentPoint.y += y;
                    else
                        currentPoint.y = y;

                    currentSegment.Add(currentPoint);
                    break;

                case 'Z': // ClosePath
                          // Add the subpath start to close the path
                    if (currentSegment.Count > 0 && !ArePointsEqual(currentPoint, subpathStart))
                    {
                        currentSegment.Add(subpathStart);
                        currentPoint = subpathStart;
                    }

                    // Add the current segment to segments and start a new segment
                    if (currentSegment.Count > 0)
                    {
                        segments.Add(currentSegment.ToArray());
                        currentSegment = new List<Vector2>();
                    }
                    break;

                case 'C': // Cubic Bezier
                    CubicBezierTo(relative, currentSegment);
                    break;

                case 'Q': // Quadratic Bezier
                    QuadraticBezierTo(relative, currentSegment);
                    break;

                default:
                    // Skip unknown commands
                    index++;
                    break;
            }
        }

        // Add any remaining segment
        if (currentSegment.Count > 0)
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
   

    private void CubicBezierTo(bool relative, List<Vector2> currentSegment)
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

        // Finalize the current segment
        currentSegment.Add(endPoint);
        currentPoint = endPoint;
    }



    private void QuadraticBezierTo(bool relative, List<Vector2> currentSegment)
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

        // Finalize the current segment
        currentSegment.Add(endPoint);
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

    // Helper method to compare points with a small epsilon for floating point comparison
    private bool ArePointsEqual(Vector2 p1, Vector2 p2, float epsilon = 0.001f)
    {
        return Vector2.Distance(p1, p2) < epsilon;
    }
}

