using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SmoothConeGenerator : MonoBehaviour
{
    [Header("Path and Shape")]
    [Tooltip("The control points that will guide the smooth central axis of the cone.")]
    public List<Vector3> controlPoints = new List<Vector3>() {
        new Vector3(0, 0, 0),
        new Vector3(0.5f, 1.5f, 0.5f),
        new Vector3(0, 3, 1f),
        new Vector3(-0.5f, 4.5f, 0.5f)
    };

    [Tooltip("Radius of the cone at its base (the start of the spline).")]
    public float baseRadius = 1f;

    [Tooltip("Number of segments to create the circular cross-sections. Higher values mean a smoother circle.")]
    public int radialSegments = 16;

    [Tooltip("Number of points to generate on the spline between each pair of control points. Higher values mean a smoother path.")]
    public int splineResolution = 10;

    [Header("Tip Smoothing")]
    [Tooltip("Enable to create a rounded tip instead of a sharp point.")]
    public bool useRoundedTip = true;

    [Tooltip("Radius of the hemispherical cap at the tip. The cone body will taper to this radius.")]
    public float tipCapRadius = 0.2f;

    [Tooltip("Number of latitude segments for the rounded tip cap. Higher values mean a smoother cap.")]
    public int tipCapLatitudeSegments = 8;


    [Header("Appearance")]
    [Tooltip("Material to apply to the generated cone.")]
    public Material coneMaterial;

    private Mesh mesh;

    void Start()
    {
        GenerateSmoothCone();
    }

    void OnValidate()
    {
        // Basic OnValidate for runtime updates; for robust editor updates, [ExecuteInEditMode] is better.
        if (Application.isPlaying && mesh != null && controlPoints != null && controlPoints.Count >= 2)
        {
            GenerateSmoothCone();
        }
    }

    public static Vector3 GetCatmullRomPosition(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        Vector3 a = 2f * p1;
        Vector3 b = p2 - p0;
        Vector3 c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
        Vector3 d = -p0 + 3f * p1 - 3f * p2 + p3;
        return 0.5f * (a + (b * t) + (c * t * t) + (d * t * t * t));
    }

    List<Vector3> GenerateSplinePath(List<Vector3> points, int resolutionPerSegment)
    {
        if (points == null || points.Count < 2) return points;
        List<Vector3> splinePoints = new List<Vector3>();
        if (points.Count == 2) {
             for (int i = 0; i <= resolutionPerSegment; i++) {
                splinePoints.Add(Vector3.Lerp(points[0], points[1], (float)i / resolutionPerSegment));
            }
            return splinePoints;
        }
        splinePoints.Add(points[0]);
        for (int i = 0; i < points.Count - 1; i++) {
            Vector3 p0 = (i == 0) ? points[i] : points[i - 1];
            Vector3 p1 = points[i];
            Vector3 p2 = points[i + 1];
            Vector3 p3 = (i == points.Count - 2) ? points[i + 1] : points[i + 2];
            for (int j = 1; j <= resolutionPerSegment; j++) {
                splinePoints.Add(GetCatmullRomPosition((float)j / resolutionPerSegment, p0, p1, p2, p3));
            }
        }
        return splinePoints;
    }

    public void GenerateSmoothCone()
    {
        if (controlPoints == null || controlPoints.Count < 2) {
            Debug.LogError("Control points must contain at least two points.");
            if (mesh != null) mesh.Clear();
            return;
        }

        List<Vector3> actualPathPoints = GenerateSplinePath(controlPoints, splineResolution);
        if (actualPathPoints == null || actualPathPoints.Count < 2) {
            Debug.LogError("Spline generation failed or resulted in too few points.");
            if (mesh != null) mesh.Clear();
            return;
        }

        GetComponent<MeshFilter>().mesh = mesh = new Mesh();
        mesh.name = "Procedural Smooth Cone";

        if (coneMaterial != null) {
            GetComponent<MeshRenderer>().material = coneMaterial;
        } else {
            var renderer = GetComponent<MeshRenderer>();
            var defaultMaterial = new Material(Shader.Find("Standard"));
            defaultMaterial.color = new Color(0.5f, 0.7f, 1.0f); // Light blue
            renderer.material = defaultMaterial;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        float totalPathLength = 0f;
        List<float> segmentLengths = new List<float>();
        for (int i = 0; i < actualPathPoints.Count - 1; i++) {
            float length = Vector3.Distance(actualPathPoints[i], actualPathPoints[i + 1]);
            segmentLengths.Add(length);
            totalPathLength += length;
        }
        if (totalPathLength <= 0.0001f) totalPathLength = 1f;

        float currentPathDistance = 0f;

        // Generate cone body vertices
        for (int i = 0; i < actualPathPoints.Count; i++)
        {
            Vector3 currentPoint = actualPathPoints[i];
            Vector3 forwardDirection;

            if (i < actualPathPoints.Count - 1) forwardDirection = (actualPathPoints[i + 1] - currentPoint).normalized;
            else if (actualPathPoints.Count > 1) forwardDirection = (currentPoint - actualPathPoints[i - 1]).normalized;
            else forwardDirection = Vector3.up;

            if (forwardDirection == Vector3.zero && i > 0) forwardDirection = (actualPathPoints[i] - actualPathPoints[i-1]).normalized;
            if (forwardDirection == Vector3.zero) forwardDirection = Vector3.up;

            Quaternion rotation = Quaternion.LookRotation(forwardDirection, Mathf.Abs(Vector3.Dot(forwardDirection, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up);

            float pathLerpFactor = (actualPathPoints.Count > 1) ? ((float)i / (actualPathPoints.Count - 1)) : 0f;
            float targetTipRadius = (useRoundedTip && tipCapRadius > 0.001f) ? tipCapRadius : 0f;
            float currentRadius = Mathf.Lerp(baseRadius, targetTipRadius, pathLerpFactor);

            // If it's the very last point and we are NOT using a rounded tip, ensure radius is zero.
            if (i == actualPathPoints.Count - 1 && !useRoundedTip) currentRadius = 0f;


            for (int j = 0; j <= radialSegments; j++)
            {
                float angle = (float)j / radialSegments * Mathf.PI * 2f;
                Vector3 localPos = new Vector3(Mathf.Cos(angle) * currentRadius, Mathf.Sin(angle) * currentRadius, 0);
                vertices.Add(currentPoint + rotation * localPos);
                // UV: v coordinate progresses along the path, u wraps around
                float vCoordinate = (float)i / (actualPathPoints.Count -1 + (useRoundedTip ? tipCapLatitudeSegments : 0) );
                uvs.Add(new Vector2((float)j / radialSegments, vCoordinate));
            }

            if (i < actualPathPoints.Count - 1) currentPathDistance += segmentLengths[i];
        }

        // Generate cone body triangles
        for (int i = 0; i < actualPathPoints.Count - 1; i++)
        {
            int baseIndex = i * (radialSegments + 1);
            int nextBaseIndex = (i + 1) * (radialSegments + 1);
            for (int j = 0; j < radialSegments; j++) {
                triangles.Add(baseIndex + j);
                triangles.Add(nextBaseIndex + j);
                triangles.Add(baseIndex + j + 1);

                triangles.Add(baseIndex + j + 1);
                triangles.Add(nextBaseIndex + j);
                triangles.Add(nextBaseIndex + j + 1);
            }
        }

        // Generate Rounded Tip Cap (if enabled)
        if (useRoundedTip && tipCapRadius > 0.001f && actualPathPoints.Count > 0)
        {
            Vector3 capStartPoint = actualPathPoints[actualPathPoints.Count - 1];
            Vector3 capForwardDir;
            if (actualPathPoints.Count > 1) capForwardDir = (actualPathPoints[actualPathPoints.Count - 1] - actualPathPoints[actualPathPoints.Count - 2]).normalized;
            else capForwardDir = Vector3.up;
            if (capForwardDir == Vector3.zero) capForwardDir = Vector3.up;

            Quaternion capRotation = Quaternion.LookRotation(capForwardDir, Mathf.Abs(Vector3.Dot(capForwardDir, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up);

            int lastConeRingStartIndex = (actualPathPoints.Count - 1) * (radialSegments + 1);

            // Add vertices for the hemisphere
            for (int lat = 1; lat <= tipCapLatitudeSegments; lat++) // Start from 1, 0 is the last cone ring
            {
                float phi = ((float)lat / tipCapLatitudeSegments) * (Mathf.PI / 2f); // Angle from base to pole of hemisphere
                float ringRadius = tipCapRadius * Mathf.Cos(phi);
                float ringOffset = tipCapRadius * Mathf.Sin(phi);

                // UV v-coordinate continuation
                float vCoordinate = (float)(actualPathPoints.Count -1 + lat) / (actualPathPoints.Count -1 + tipCapLatitudeSegments);


                for (int lon = 0; lon <= radialSegments; lon++)
                {
                    float theta = ((float)lon / radialSegments) * (Mathf.PI * 2f);
                    Vector3 localPos = new Vector3(Mathf.Cos(theta) * ringRadius, Mathf.Sin(theta) * ringRadius, 0);
                    vertices.Add(capStartPoint + capRotation * (localPos + new Vector3(0,0,ringOffset)) );
                    uvs.Add(new Vector2((float)lon / radialSegments, vCoordinate));
                }
            }

            // Triangles for the cap
            // Stitch last cone ring to first cap ring
            int firstCapRingStartIndex = vertices.Count - (tipCapLatitudeSegments * (radialSegments + 1));

            for (int lon = 0; lon < radialSegments; lon++)
            {
                triangles.Add(lastConeRingStartIndex + lon);
                triangles.Add(firstCapRingStartIndex + lon);
                triangles.Add(lastConeRingStartIndex + lon + 1);

                triangles.Add(lastConeRingStartIndex + lon + 1);
                triangles.Add(firstCapRingStartIndex + lon);
                triangles.Add(firstCapRingStartIndex + lon + 1);
            }


            // Triangles for subsequent cap rings
            for (int lat = 0; lat < tipCapLatitudeSegments - 1; lat++)
            {
                int ring1StartIndex = firstCapRingStartIndex + lat * (radialSegments + 1);
                int ring2StartIndex = firstCapRingStartIndex + (lat + 1) * (radialSegments + 1);
                for (int lon = 0; lon < radialSegments; lon++)
                {
                    triangles.Add(ring1StartIndex + lon);
                    triangles.Add(ring2StartIndex + lon);
                    triangles.Add(ring1StartIndex + lon + 1);

                    triangles.Add(ring1StartIndex + lon + 1);
                    triangles.Add(ring2StartIndex + lon);
                    triangles.Add(ring2StartIndex + lon + 1);
                }
            }

            // Triangle fan for the pole of the cap
            int beforePoleRingStartIndex = vertices.Count - (radialSegments + 1);
            int poleIndex = vertices.Count; // This will be the actual tip vertex
            vertices.Add(capStartPoint + capRotation * new Vector3(0,0,tipCapRadius));
            float vCoordinatePole = 1.0f; // (float)(actualPathPoints.Count -1 + tipCapLatitudeSegments) / (actualPathPoints.Count -1 + tipCapLatitudeSegments);
            uvs.Add(new Vector2(0.5f, vCoordinatePole)); // UV for the pole

            for (int lon = 0; lon < radialSegments; lon++)
            {
                triangles.Add(beforePoleRingStartIndex + lon);
                triangles.Add(poleIndex);
                triangles.Add(beforePoleRingStartIndex + lon + 1);
            }
        }


        // Base Cap
        if (baseRadius > 0.001f && actualPathPoints.Count > 0)
        {
            int baseCenterIndex = vertices.Count;
            vertices.Add(actualPathPoints[0]); // Center of the base
            uvs.Add(new Vector2(0.5f, 0f)); // UV for center of base

            // Triangles for base cap (winding order for outward normal)
            for (int j = 0; j < radialSegments; j++)
            {
                triangles.Add(baseCenterIndex);
                triangles.Add(j + 1);           // Outer ring vertices
                triangles.Add(j);
            }
        }

        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals(); // Crucial for smooth appearance
        mesh.RecalculateBounds();
    }
}