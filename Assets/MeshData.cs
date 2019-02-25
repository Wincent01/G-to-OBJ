using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
///     Mesh data for G and OBJ files.
/// </summary>
public class MeshData
{
    private const int GFileHeader = 0x42473031;
    private readonly string _brickId;
    public readonly List<Mesh> Meshes = new List<Mesh>();
    private bool _brickHasUVs;

    private int _startIndex;

    /// <summary>
    ///     Load mesh data from a G or OBJ file.
    /// </summary>
    /// <param name="entryFiles">List only applicable on G files.</param>
    public MeshData(IReadOnlyList<string> entryFiles)
    {
        _brickId = Path.GetFileNameWithoutExtension(entryFiles[0]);
        foreach (var file in entryFiles) Meshes.Add(Manager.ToG ? LoadMeshFromObj(file) : LoadMeshFromG(file));
    }

    /// <summary>
    ///     Load mesh from .g file.
    /// </summary>
    /// <param name="filePath">Path to .g file to load.</param>
    /// <returns>Mesh data</returns>
    private Mesh LoadMeshFromG(string filePath)
    {
        var mesh = new Mesh();

        var attr = File.GetAttributes(filePath);
        if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(filePath, attr & ~ FileAttributes.ReadOnly);

        var fileStream = new FileStream(filePath, FileMode.Open);
        var binaryReader = new BinaryReader(fileStream);

        // Read basic mesh info
        binaryReader.BaseStream.Position = 4; // Skip header
        var vertexCount = binaryReader.ReadUInt32();
        var indexCount = binaryReader.ReadUInt32();
        var flags = (Flags) binaryReader.ReadUInt32();
        //Debug.Log(Path.GetFileName(filePath) + " flags: " + flags);

        // Read vertices
        mesh.Vertices = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
            mesh.Vertices[i] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(),
                binaryReader.ReadSingle());

        // Read normals if they exist
        mesh.Normals = new Vector3[vertexCount];
        if ((flags & Flags.Normals) == Flags.Normals)
        {
            for (var i = 0; i < vertexCount; i++)
                mesh.Normals[i] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(),
                    binaryReader.ReadSingle());
        }
        // If no normals exist, use dummy normals - not aware of any g files that lack normals but it's theoretically possible
        else
        {
            Debug.LogWarning(Path.GetFileName(filePath) + " has no normals");
            for (var i = 0; i < vertexCount; i++) mesh.Normals[i] = Vector3.up;
        }

        // Read UVs if they exist
        mesh.Uv = new Vector2[vertexCount];
        if ((flags & Flags.Uv) == Flags.Uv)
        {
            _brickHasUVs = true;
            for (var i = 0; i < vertexCount; i++)
                mesh.Uv[i] = new Vector2(binaryReader.ReadSingle(), -binaryReader.ReadSingle() + 1.0f);
        }
        // Set UVs to zero if this mesh lacks them (will only be used if other meshes for the brick *do* have UVs)
        else
        {
            for (var i = 0; i < vertexCount; i++) mesh.Uv[i] = Vector2.zero;
        }

        // Read triangles
        mesh.Triangles = new int[indexCount];
        for (var i = 0; i < indexCount; i++) mesh.Triangles[i] = (int) binaryReader.ReadUInt32();

        // Done reading
        binaryReader.Close();
        // According to some old stackoverflow post just closing the reader should (might) be enough, but just in case
        fileStream.Close();
        fileStream.Dispose();

        return mesh;
    }

    /// <summary>
    ///     Load mesh data from OBJ file.
    /// </summary>
    /// <param name="filePath">Path to .obj file to load.</param>
    /// <returns>Mesh data</returns>
    private static Mesh LoadMeshFromObj(string filePath)
    {
        var data = File.ReadAllLines(filePath);

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        var uvs = new List<Vector2>();

        /*
         * There might be a more formal way of doing this part, but, this works.
         */

        foreach (var line in data)
        {
            var parts = line.Split(' ');
            switch (parts[0])
            {
                case "v":
                    vertices.Add(new Vector3(float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3])));
                    break;
                case "vn":
                    normals.Add(new Vector3(
                        (float) decimal.Parse(parts[1], NumberStyles.Float),
                        (float) decimal.Parse(parts[2], NumberStyles.Float),
                        (float) decimal.Parse(parts[3], NumberStyles.Float)));
                    break;
                case "f":
                    triangles.AddRange(new[]
                    {
                        int.Parse(parts[1].Split('/')[0]),
                        int.Parse(parts[2].Split('/')[0]),
                        int.Parse(parts[3].Split('/')[0])
                    });
                    break;
                case "vt":
                    uvs.Add(new Vector2(float.Parse(parts[1]), float.Parse(parts[2])));
                    break;
            }
        }

        return new Mesh
        {
            Normals = normals.ToArray(),
            Vertices = vertices.ToArray(),
            Triangles = triangles.ToArray(),
            Uv = uvs.ToArray()
        };
    }

    /// <summary>
    ///     Export mesh data to OBJ file.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Out Of Range ConversionModes</exception>
    public void ExportToObj()
    {
        var meshString = new StringBuilder();
        switch (Manager.ConversionMode)
        {
            case ConversionModes.Default:
                for (var i = 0; i < Meshes.Count; i++)
                {
                    meshString.Append("\ng ").Append("g" + i).Append("\n");
                    meshString.Append(MeshToString(Meshes[i], _brickHasUVs));
                }

                break;
            case ConversionModes.NoUVs:
                for (var i = 0; i < Meshes.Count; i++)
                {
                    meshString.Append("\ng ").Append("g" + i).Append("\n");
                    meshString.Append(MeshToString(Meshes[i], false));
                }

                break;
            case ConversionModes.NoUVsAndNoGroups:
                meshString.Append(AllMeshesToString());
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        File.WriteAllText(Manager.OutputDirectory + Path.DirectorySeparatorChar + _brickId + ".obj",
            meshString.ToString());
        //Debug.Log("Saved file " + brickID + ".obj");
    }

    /// <summary>
    ///     Export mesh data to G file.
    /// </summary>
    public void ExportToG()
    {
        var path = Manager.OutputDirectory + Path.DirectorySeparatorChar + _brickId + ".g";
        if (File.Exists(path))
            File.Delete(path);
        File.Create(path).Dispose();
        var fileStream = new FileStream(path, FileMode.Open);
        var binaryWriter = new BinaryWriter(fileStream);

        var mesh = Meshes[0];

        //Write header
        binaryWriter.Write(GFileHeader);
        binaryWriter.Write(mesh.Vertices.Length);
        binaryWriter.Write(mesh.Triangles.Length);
        binaryWriter.Write((int) (Flags.Normals | (mesh.Uv.Length > 0 ? Flags.Uv : 0)));

        //Write Vertices
        foreach (var vertex in mesh.Vertices)
        {
            binaryWriter.Write(vertex.x);
            binaryWriter.Write(vertex.y);
            binaryWriter.Write(vertex.z);
        }

        //Write Normals
        foreach (var normal in mesh.Normals)
        {
            binaryWriter.Write(normal.x);
            binaryWriter.Write(normal.y);
            binaryWriter.Write(normal.z);
        }

        //Write UV
        foreach (var vertex in mesh.Uv)
        {
            binaryWriter.Write(vertex.x);
            binaryWriter.Write(-vertex.y - 1f);
        }

        //Write Triangles
        foreach (var triangle in mesh.Triangles) binaryWriter.Write(triangle - 1);

        //End stream
        binaryWriter.Close();
        fileStream.Close();
        fileStream.Dispose();
    }

    /// <summary>
    ///     Build a string in OBJ format from mesh data.
    /// </summary>
    /// <param name="m">Mesh data.</param>
    /// <param name="includeUVs">Include UV data.</param>
    /// <returns>String in OBJ format</returns>
    private string MeshToString(Mesh m, bool includeUVs)
    {
        var sb = new StringBuilder();

        foreach (var v in m.Vertices) sb.Append(string.Format("v {0} {1} {2}\n", v.x, v.y, v.z));

        sb.Append("\n");

        foreach (var v in m.Normals) sb.Append(string.Format("vn {0} {1} {2}\n", v.x, v.y, v.z));

        sb.Append("\n");
        if (includeUVs)
        {
            foreach (Vector3 v in m.Uv) sb.Append(string.Format("vt {0} {1}\n", v.x, v.y));

            sb.Append("\n");
        }

        if (includeUVs)
            for (var i = 0; i < m.Triangles.Length; i += 3)
                sb.Append(string.Format("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n",
                    m.Triangles[i] + 1 + _startIndex, m.Triangles[i + 1] + 1 + _startIndex,
                    m.Triangles[i + 2] + 1 + _startIndex));
        else
            for (var i = 0; i < m.Triangles.Length; i += 3)
                sb.Append(string.Format("f {0}//{0} {1}//{1} {2}//{2}\n",
                    m.Triangles[i] + 1 + _startIndex, m.Triangles[i + 1] + 1 + _startIndex,
                    m.Triangles[i + 2] + 1 + _startIndex));

        _startIndex += m.Vertices.Length;
        return sb.ToString();
    }

    /// <summary>
    ///     Build a string in OBJ format of the entire mesh.
    /// </summary>
    /// <returns>String in OBJ format</returns>
    private string AllMeshesToString()
    {
        var sb = new StringBuilder();

        foreach (var m in Meshes)
        foreach (var v in m.Vertices)
            sb.Append(string.Format("v {0} {1} {2}\n", v.x, v.y, v.z));

        sb.Append("\n");

        foreach (var m in Meshes)
        foreach (var v in m.Normals)
            sb.Append(string.Format("vn {0} {1} {2}\n", v.x, v.y, v.z));

        sb.Append("\n");
        foreach (var m in Meshes)
        {
            for (var i = 0; i < m.Triangles.Length; i += 3)
                sb.Append(string.Format("f {0}//{0} {1}//{1} {2}//{2}\n",
                    m.Triangles[i] + 1 + _startIndex, m.Triangles[i + 1] + 1 + _startIndex,
                    m.Triangles[i + 2] + 1 + _startIndex));

            _startIndex += m.Vertices.Length;
        }

        return sb.ToString();
    }

    // Using Unity's own Mesh class in OBJ exporting is really slow for some reason, so here's a lightweight class that just holds the data we want and doesn't do anything mysterious
    public class Mesh
    {
        public Vector3[] Normals;
        public int[] Triangles;
        public Vector2[] Uv;
        public Vector3[] Vertices;
    }
}