using UnityEngine;
using System.Collections.Generic;
using System.Xml;
using System.Globalization;

[RequireComponent(typeof(Camera))]
public class TektronixSVGRenderer : MonoBehaviour
{
    [Header("SVG Input")]
    [SerializeField] private TextAsset svgFile;
    [SerializeField] private string svgFilePath; // New field for SVG file path
    [SerializeField] private float scaleFactor = 1.0f;

    [Header("Drawing Settings")]
    [SerializeField] private float drawSpeed = 10.0f; // Lines per second
    [ColorUsage(true, true)][SerializeField] private Color solidColor = Color.green; // Solid color for previous lines (fallback)
    [ColorUsage(true, true)]
    [SerializeField] private Color gradientStartColor = Color.white; // Gradient start color (fallback)
    [ColorUsage(true, true)]
    [SerializeField] private Color gradientEndColor = Color.green;   // Gradient end color (fallback)
    [SerializeField] private float lineWidthPixels = 2.0f; // Line thickness in pixels
    [SerializeField] private bool useElementColors = true; // Whether to use colors from SVG elements

    [Header("Compute Shader")]
    [SerializeField] private ComputeShader lineDrawingShader;

    [Header("Output")]
    [SerializeField] private RenderTexture outputTexture; // Exposed RenderTexture

    private List<Vector2> points = new List<Vector2>();
    private List<int> optimizedPath = new List<int>();
    private List<Color> pointColors = new List<Color>(); // Colors for each point
    private ComputeBuffer pointsBuffer;
    private ComputeBuffer pathBuffer;
    private ComputeBuffer colorBuffer; // New buffer for colors
    private int kernelHandle;
    private int currentLineIndex = 0;
    private float drawTimer = 0f;

    private Vector2 svgSize = new Vector2(0, 0); // Size of the SVG canvas

    private void Start()
    {
        if ((string.IsNullOrEmpty(svgFilePath) && svgFile == null) || lineDrawingShader == null || outputTexture == null)
        {
            Debug.LogError("SVG file, SVG file path, Compute Shader, or Output Texture is missing!");
            return;
        }

        // Configure the output texture for HDR
        ConfigureHDRRenderTexture();

        ParseSVG();
        NormalizePointsToFitTexture();
        OptimizePath();
        SetupComputeShader();
        ClearRenderTexture(); // Clear the texture at the start
    }

    // New method to ensure HDR is enabled
    private void ConfigureHDRRenderTexture()
    {
        // Release the texture if it already exists
        if (outputTexture != null)
            outputTexture.Release();

        // Create a new HDR-enabled render texture with the same dimensions
        int width = outputTexture.width;
        int height = outputTexture.height;

        RenderTextureDescriptor desc = new RenderTextureDescriptor(width, height);
        desc.enableRandomWrite = true;
        desc.colorFormat = RenderTextureFormat.DefaultHDR; // Use HDR format
        desc.sRGB = false; // Linear color space for better HDR range
        desc.depthBufferBits = 0; // No need for depth buffer

        // Apply the new descriptor to the texture
        outputTexture.descriptor = desc;
        outputTexture.Create();

        Debug.Log("HDR render texture configured successfully");
    }

    // Update the Update method to use element colors when available
    private void Update()
    {
        if (currentLineIndex < optimizedPath.Count - 1)
        {
            drawTimer += Time.deltaTime * drawSpeed;
            int linesToDraw = Mathf.FloorToInt(drawTimer);

            if (linesToDraw > 0)
            {
                currentLineIndex = Mathf.Min(currentLineIndex + linesToDraw, optimizedPath.Count - 1);
                drawTimer -= linesToDraw;



                // Draw all lines up to the current index
                lineDrawingShader.SetInt("currentLineIndex", currentLineIndex);

                // Ensure all colors have alpha=1.0
                Color startColorOpaque = new Color(gradientStartColor.r, gradientStartColor.g, gradientStartColor.b, 1.0f);
                Color endColorOpaque = new Color(gradientEndColor.r, gradientEndColor.g, gradientEndColor.b, 1.0f);
                Color solidColorOpaque = new Color(solidColor.r, solidColor.g, solidColor.b, 1.0f);

                lineDrawingShader.SetVector("startColor", (Vector4)startColorOpaque);
                lineDrawingShader.SetVector("endColor", (Vector4)endColorOpaque);
                lineDrawingShader.SetVector("solidColor", (Vector4)solidColorOpaque);
                lineDrawingShader.SetInt("useElementColors", useElementColors ? 1 : 0);

                // Calculate correct dispatch size
                int dispatchSize = Mathf.Max(1, Mathf.CeilToInt(currentLineIndex / 64.0f));
                lineDrawingShader.Dispatch(kernelHandle, dispatchSize, 1, 1);
            }
        } else
        {
            Debug.Log("All lines drawn.");
        }
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

    // Parse color from SVG hex string
    private Color ParseColor(string colorStr)
    {
        if (string.IsNullOrEmpty(colorStr))
            return Color.black;

        // Handle named colors
        if (colorStr.ToLower() == "black") return solidColor;
        if (colorStr.ToLower() == "white") return Color.white;
        if (colorStr.ToLower() == "red") return Color.red;
        if (colorStr.ToLower() == "green") return Color.green;
        if (colorStr.ToLower() == "blue") return Color.blue;
        if (colorStr.ToLower() == "yellow") return Color.yellow;
        if (colorStr.ToLower() == "cyan") return Color.cyan;
        if (colorStr.ToLower() == "magenta") return Color.magenta;
        if (colorStr.ToLower() == "gray" || colorStr.ToLower() == "grey") return Color.gray;

        // Remove any leading # character
        if (colorStr.StartsWith("#"))
            colorStr = colorStr.Substring(1);

        try
        {
            // Handle short form (#RGB)
            if (colorStr.Length == 3)
            {
                string r = colorStr.Substring(0, 1);
                string g = colorStr.Substring(1, 1);
                string b = colorStr.Substring(2, 1);
                colorStr = r + r + g + g + b + b;
            }

            // Check if the string is valid hex
            if (colorStr.Length != 6 || !System.Text.RegularExpressions.Regex.IsMatch(colorStr, "^[0-9A-Fa-f]{6}$"))
            {
                Debug.LogWarning($"Invalid color format: '{colorStr}'. Defaulting to black.");
                return Color.black;
            }

            // Parse as hex
            int hexValue;
            if (int.TryParse(colorStr, System.Globalization.NumberStyles.HexNumber, null, out hexValue))
            {
                float r = ((hexValue >> 16) & 0xFF) / 255f;
                float g = ((hexValue >> 8) & 0xFF) / 255f;
                float b = (hexValue & 0xFF) / 255f;
                return new Color(r, g, b, 1f);
            }
            else
            {
                Debug.LogWarning($"Failed to parse color: '{colorStr}'. Defaulting to black.");
                return Color.black;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Error parsing color '{colorStr}': {e.Message}. Defaulting to black.");
            return Color.black;
        }
    }

    // Check if a color is black (#000000)
    private bool IsBlack(Color color)
    {
        return color.r < 0.01f && color.g < 0.01f && color.b < 0.01f;
    }

    private void ParseSVG()
    {
        XmlDocument doc = new XmlDocument();

        // Load SVG from file path if provided, otherwise use TextAsset
        if (!string.IsNullOrEmpty(svgFilePath))
        {
            try
            {
                doc.Load(svgFilePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load SVG from path '{svgFilePath}': {e.Message}");
                return;
            }
        }
        else if (svgFile != null)
        {
            doc.LoadXml(svgFile.text);
        }
        else
        {
            Debug.LogError("No SVG file or path provided.");
            return;
        }





        // Handle namespaces in the SVG file
        XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
        nsmgr.AddNamespace("svg", "http://www.w3.org/2000/svg");

        // Locate the <svg> element
        XmlNode svgNode = doc.SelectSingleNode("//svg:svg", nsmgr);
        if (svgNode != null)
        {
            string widthAttr = svgNode.Attributes["width"]?.Value;
            string heightAttr = svgNode.Attributes["height"]?.Value;
            string viewBoxAttr = svgNode.Attributes["viewBox"]?.Value;

            if (!string.IsNullOrEmpty(widthAttr) && !string.IsNullOrEmpty(heightAttr))
            {
                svgSize = new Vector2(
                    float.Parse(widthAttr, CultureInfo.InvariantCulture),
                    float.Parse(heightAttr, CultureInfo.InvariantCulture)
                );
            }
            else if (!string.IsNullOrEmpty(viewBoxAttr))
            {
                string[] viewBoxValues = viewBoxAttr.Split(' ');
                if (viewBoxValues.Length == 4)
                {
                    svgSize = new Vector2(
                        float.Parse(viewBoxValues[2], CultureInfo.InvariantCulture),
                        float.Parse(viewBoxValues[3], CultureInfo.InvariantCulture)
                    );
                }
            }
            else
            {
                Debug.LogWarning("SVG canvas size not found. Defaulting to (1, 1).");
                svgSize = new Vector2(1, 1); // Default size
            }

            Debug.Log($"SVG canvas size: {svgSize.x}x{svgSize.y}");
        }
        else
        {
            Debug.LogError("No <svg> element found in the file.");
            return;
        }















        XmlNodeList paths = doc.GetElementsByTagName("path");
        foreach (XmlNode pathNode in paths)
        {
            string dAttribute = pathNode.Attributes["d"]?.Value;

            if (!string.IsNullOrEmpty(dAttribute))
            {
                Debug.Log($"pathing...");

                // Get color
                string strokeAttr = pathNode.Attributes["stroke"]?.Value;
                Color strokeColor = solidColor;

                if (!string.IsNullOrEmpty(strokeAttr))
                {
                    strokeColor = ParseColor(strokeAttr);
                }
                else
                {
                    strokeAttr = pathNode.Attributes["fill"]?.Value;
                    if (!string.IsNullOrEmpty(strokeAttr) && !IsBlack(ParseColor(strokeAttr)))
                    {
                        strokeColor = ParseColor(strokeAttr);
                    }
                }

                // Parse the path data
                SVGPathParser parser = new SVGPathParser();
                List<Vector2[]> segments = parser.ParsePath(dAttribute);

                // Add each segment separately 
                foreach (var segment in segments)
                {


                    // Add each point pair in this segment
                    for (int i = 0; i < segment.Length - 1; i++)
                    {

                        points.Add(segment[i]);
                        points.Add(segment[i + 1]);

                        // Add colors
                        pointColors.Add(strokeColor);
                        pointColors.Add(strokeColor);
                    }
                }
            }
        }

        // Parse <line> elements
        XmlNodeList lines = doc.GetElementsByTagName("line");
        foreach (XmlNode lineNode in lines)
        {
            float x1 = float.Parse(lineNode.Attributes["x1"].Value);
            float y1 = float.Parse(lineNode.Attributes["y1"].Value);
            float x2 = float.Parse(lineNode.Attributes["x2"].Value);
            float y2 = float.Parse(lineNode.Attributes["y2"].Value);

            // Get stroke color
            string strokeAttr = lineNode.Attributes["stroke"]?.Value;
            Color strokeColor = solidColor;
            if (!string.IsNullOrEmpty(strokeAttr))
            {
                strokeColor = ParseColor(strokeAttr);
            }

            points.Add(new Vector2(x1, y1));
            points.Add(new Vector2(x2, y2));

            // Add color for both points
            pointColors.Add(strokeColor);
            pointColors.Add(strokeColor);
        }

        // Parse <rect> elements
        XmlNodeList rects = doc.GetElementsByTagName("rect");
        foreach (XmlNode rectNode in rects)
        {
            float x = float.Parse(rectNode.Attributes["x"]?.Value ?? "0");
            float y = float.Parse(rectNode.Attributes["y"]?.Value ?? "0");
            float width = float.Parse(rectNode.Attributes["width"].Value);
            float height = float.Parse(rectNode.Attributes["height"].Value);

            // Get stroke color
            string strokeAttr = rectNode.Attributes["stroke"]?.Value;
            Color strokeColor = solidColor;
            if (!string.IsNullOrEmpty(strokeAttr))
            {
                strokeColor = ParseColor(strokeAttr);
            }

            // Add rectangle edges as lines
            points.Add(new Vector2(x, y));
            points.Add(new Vector2(x + width, y));
            pointColors.Add(strokeColor);
            pointColors.Add(strokeColor);

            points.Add(new Vector2(x + width, y));
            points.Add(new Vector2(x + width, y + height));
            pointColors.Add(strokeColor);
            pointColors.Add(strokeColor);

            points.Add(new Vector2(x + width, y + height));
            points.Add(new Vector2(x, y + height));
            pointColors.Add(strokeColor);
            pointColors.Add(strokeColor);

            points.Add(new Vector2(x, y + height));
            points.Add(new Vector2(x, y));
            pointColors.Add(strokeColor);
            pointColors.Add(strokeColor);
        }

        Debug.Log($"Parsed {points.Count} points from SVG.");
    }

    private void NormalizePointsToFitTexture()
    {
        if (points.Count == 0)
        {
            Debug.LogWarning("No points to normalize.");
            return;
        }

        if (svgSize.x <= 0 || svgSize.y <= 0)
        {
            Debug.LogError("Invalid SVG canvas size. Ensure the SVG file specifies valid dimensions.");
            return;
        }

        // Get the texture dimensions
        float textureWidth = outputTexture.width;
        float textureHeight = outputTexture.height;

        // Calculate the scale factors for X and Y based on the ratio of texture size to SVG canvas size
        float scaleX = textureWidth / svgSize.x;
        float scaleY = textureHeight / svgSize.y;

        // Normalize points to fit the texture and flip the Y-axis
        for (int i = 0; i < points.Count; i++)
        {
            points[i] = new Vector2(
                points[i].x * scaleX,                          // Scale X position
                textureHeight - (points[i].y * scaleY)         // Scale Y position and flip Y-axis
            );
        }

        Debug.Log("Points scaled to match the texture size.");
    }





    private void OptimizePath()
    {        
        
        // Group points into subpaths
        List<List<int>> subpaths = new List<List<int>>();
        for (int i = 0; i < points.Count; i += 2)
        {
            subpaths.Add(new List<int> { i, i + 1 });
        }

        // Flatten subpaths into a single optimized path with break markers
        optimizedPath.Clear();
        foreach (var subpath in subpaths)
        {
            optimizedPath.AddRange(subpath);
            optimizedPath.Add(-1); // Add a break marker after each subpath
        }

    }

    private void SetupComputeShader()
    {
        kernelHandle = lineDrawingShader.FindKernel("CSDrawLines");

        // Create and set the points buffer
        pointsBuffer = new ComputeBuffer(points.Count, sizeof(float) * 2);
        pointsBuffer.SetData(points);

        // Create and set the path buffer
        pathBuffer = new ComputeBuffer(optimizedPath.Count, sizeof(int));
        pathBuffer.SetData(optimizedPath);

        // Create and set the color buffer
        if (pointColors.Count > 0)
        {
            colorBuffer = new ComputeBuffer(pointColors.Count, sizeof(float) * 4);
            colorBuffer.SetData(pointColors);
            lineDrawingShader.SetBuffer(kernelHandle, "pointColors", colorBuffer);
        }

        // Ensure the output texture is configured for random write
        if (!outputTexture.IsCreated())
            outputTexture.Create();

        lineDrawingShader.SetBuffer(kernelHandle, "points", pointsBuffer);
        lineDrawingShader.SetBuffer(kernelHandle, "path", pathBuffer);
        lineDrawingShader.SetTexture(kernelHandle, "Result", outputTexture);
        lineDrawingShader.SetInt("pointCount", points.Count);
        lineDrawingShader.SetFloat("lineWidthPixels", lineWidthPixels);
    }

    // Update clear method for HDR
    private void ClearRenderTexture()
    {
        RenderTexture activeRT = RenderTexture.active;
        RenderTexture.active = outputTexture;
        GL.Clear(true, true, new Color(0, 0, 0, 1)); // Clear to black with alpha=1
        RenderTexture.active = activeRT;
    }

    private void ReleaseBuffers()
    {
        if (pointsBuffer != null) pointsBuffer.Release();
        if (pathBuffer != null) pathBuffer.Release();
        if (colorBuffer != null) colorBuffer.Release();
    }
}
