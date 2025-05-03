using UnityEngine;
using System.Collections.Generic;
using System.Xml;

[RequireComponent(typeof(Camera))]
public class TektronixSVGRenderer : MonoBehaviour
{
    [Header("SVG Input")]
    [SerializeField] private TextAsset svgFile;
    [SerializeField] private float scaleFactor = 1.0f;

    [Header("Drawing Settings")]
    [SerializeField] private float drawSpeed = 10.0f; // Lines per second
    [SerializeField] private Color solidColor = Color.green; // Solid color for previous lines
    [SerializeField] private Color gradientStartColor = Color.white; // Gradient start color
    [SerializeField] private Color gradientEndColor = Color.green;   // Gradient end color
    [SerializeField] private float lineWidthPixels = 2.0f; // Line thickness in pixels

    [Header("Compute Shader")]
    [SerializeField] private ComputeShader lineDrawingShader;

    [Header("Output")]
    [SerializeField] private RenderTexture outputTexture; // Exposed RenderTexture

    private List<Vector2> points = new List<Vector2>();
    private List<int> optimizedPath = new List<int>();
    private ComputeBuffer pointsBuffer;
    private ComputeBuffer pathBuffer;
    private int kernelHandle;
    private int currentLineIndex = 0;
    private float drawTimer = 0f;

    private void Start()
    {
        if (svgFile == null || lineDrawingShader == null || outputTexture == null)
        {
            Debug.LogError("SVG file, Compute Shader, or Output Texture is missing!");
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


    // Update the Update method to ensure alpha is always 1.0
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

                // Clear the render texture for a fresh draw
                ClearRenderTexture();

                // Draw all lines up to the current index
                lineDrawingShader.SetInt("currentLineIndex", currentLineIndex);

                // Ensure all colors have alpha=1.0
                Color startColorOpaque = new Color(gradientStartColor.r, gradientStartColor.g, gradientStartColor.b, 1.0f);
                Color endColorOpaque = new Color(gradientEndColor.r, gradientEndColor.g, gradientEndColor.b, 1.0f);
                Color solidColorOpaque = new Color(solidColor.r, solidColor.g, solidColor.b, 1.0f);

                lineDrawingShader.SetVector("startColor", (Vector4)startColorOpaque);
                lineDrawingShader.SetVector("endColor", (Vector4)endColorOpaque);
                lineDrawingShader.SetVector("solidColor", (Vector4)solidColorOpaque);

                // Calculate correct dispatch size
                int dispatchSize = Mathf.Max(1, Mathf.CeilToInt(currentLineIndex / 64.0f));
                lineDrawingShader.Dispatch(kernelHandle, dispatchSize, 1, 1);
            }
        }
    }


    private void OnDestroy()
    {
        ReleaseBuffers();
    }

    private void ParseSVG()
    {
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(svgFile.text);

        // Parse <path> elements
        XmlNodeList paths = doc.GetElementsByTagName("path");
        foreach (XmlNode pathNode in paths)
        {
            string dAttribute = pathNode.Attributes["d"]?.Value;
            if (!string.IsNullOrEmpty(dAttribute))
            {
                SVGPathParser parser = new SVGPathParser();
                List<Vector2[]> segments = parser.ParsePath(dAttribute);

                foreach (var segment in segments)
                {
                    for (int i = 0; i < segment.Length - 1; i++)
                    {
                        points.Add(segment[i]);
                        points.Add(segment[i + 1]);
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

            points.Add(new Vector2(x1, y1));
            points.Add(new Vector2(x2, y2));
        }

        // Parse <rect> elements
        XmlNodeList rects = doc.GetElementsByTagName("rect");
        foreach (XmlNode rectNode in rects)
        {
            float x = float.Parse(rectNode.Attributes["x"]?.Value ?? "0");
            float y = float.Parse(rectNode.Attributes["y"]?.Value ?? "0");
            float width = float.Parse(rectNode.Attributes["width"].Value);
            float height = float.Parse(rectNode.Attributes["height"].Value);

            // Add rectangle edges as lines
            points.Add(new Vector2(x, y));
            points.Add(new Vector2(x + width, y));

            points.Add(new Vector2(x + width, y));
            points.Add(new Vector2(x + width, y + height));

            points.Add(new Vector2(x + width, y + height));
            points.Add(new Vector2(x, y + height));

            points.Add(new Vector2(x, y + height));
            points.Add(new Vector2(x, y));
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

        float textureWidth = outputTexture.width;
        float textureHeight = outputTexture.height;

        // Find the bounds of the SVG points
        Vector2 min = points[0];
        Vector2 max = points[0];
        foreach (var point in points)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        // Calculate scale and offset to fit points into the texture
        Vector2 size = max - min;
        float scale = Mathf.Min(textureWidth / size.x, textureHeight / size.y) * scaleFactor;
        Vector2 offset = new Vector2(-min.x * scale, -min.y * scale);

        // Normalize points and flip the Y-axis
        for (int i = 0; i < points.Count; i++)
        {
            points[i] = new Vector2(points[i].x * scale + offset.x, textureHeight - (points[i].y * scale + offset.y));
        }
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



    // Update the setup method to ensure alpha is correctly set
    private void SetupComputeShader()
    {
        kernelHandle = lineDrawingShader.FindKernel("CSDrawLines");

        // Create and set the points buffer
        pointsBuffer = new ComputeBuffer(points.Count, sizeof(float) * 2);
        pointsBuffer.SetData(points);

        // Create and set the path buffer
        pathBuffer = new ComputeBuffer(optimizedPath.Count, sizeof(int));
        pathBuffer.SetData(optimizedPath);

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
    }
}
