using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public enum State
{
    Start,
    Converting,
    Done
}

public enum ConversionModes
{
    Default,
    NoUVs,
    NoUVsAndNoGroups
}

[Flags]
public enum Flags
{
    None = 0,
    UV = 1, // 0x01
    Normals = 2, // 0x02
    Flexible = 4, // 0x08
    Unknown8 = 8, // 0x04
    Unknown16 = 16, // 0x10
    Unknown32 = 32 // 0x20
}

public class Manager : MonoBehaviour
{
    // inspector
    [SerializeField] [FormerlySerializedAs("BrickMaterial")]
    private Material _brickMaterial;

    // general
    private State _state;
    private string[] _inputFiles;
    private int _currentFile;
    private DateTime _startTime;
    private TimeSpan _timeSpan;

    // user input
    private string _inputDirectory = @"C:\Some\Directory";
    private string _outputDirectory = @"C:\Another\Directory";
    private ConversionModes _conversionMode;

    // reset these per frame/brick
    private readonly List<Mesh> _meshes = new List<Mesh>();
    private readonly List<GameObject> _meshGameObjects = new List<GameObject>();
    private string _brickId;
    private bool _brickHasUVs;
    private int _startIndex;

    private void Start()
    {
        if (PlayerPrefs.HasKey("Input Directory")) _inputDirectory = PlayerPrefs.GetString("Input Directory");

        if (PlayerPrefs.HasKey("Output Directory")) _outputDirectory = PlayerPrefs.GetString("Output Directory");

        if (PlayerPrefs.HasKey("Conversion Mode"))
            _conversionMode = (ConversionModes) PlayerPrefs.GetInt("Conversion Mode");
    }

    private void OnGUI()
    {
        switch (_state)
        {
            case State.Start:
                GUI.Box(new Rect(10, 10, 250, 220), "");

                GUI.Label(new Rect(15, 10, 240, 25), "Input directory:");
                _inputDirectory = GUI.TextField(new Rect(15, 30, 240, 25), _inputDirectory, 100);

                GUI.Label(new Rect(15, 60, 240, 25), "Output directory:");
                _outputDirectory = GUI.TextField(new Rect(15, 80, 240, 25), _outputDirectory, 100);

                GUI.Label(new Rect(15, 110, 240, 25), "Conversion mode:");
                var conversionModeOptions = new[] {" Default", " No UVs", " No UVs, no groups"};
                _conversionMode = (ConversionModes) GUI.SelectionGrid(new Rect(15, 130, 240, 62), (int) _conversionMode,
                    conversionModeOptions, 1, "toggle");

                if (GUI.Button(new Rect(15, 200, 240, 25), "Go"))
                {
                    if (!Directory.Exists(_inputDirectory))
                    {
                        _inputDirectory = "Directory doesn't exist";
                        break;
                    }

                    _inputFiles = Directory.GetFiles(_inputDirectory, "*.g");
                    //Debug.Log(inputFiles.Length + " bricks");
                    if (_inputFiles.Length == 0)
                    {
                        _inputDirectory = "No .g files found";
                        break;
                    }

                    if (!Directory.Exists(_outputDirectory)) Directory.CreateDirectory(_outputDirectory);

                    PlayerPrefs.SetString("Input Directory", _inputDirectory);
                    PlayerPrefs.SetString("Output Directory", _outputDirectory);
                    PlayerPrefs.SetInt("Conversion Mode", (int) _conversionMode);
                    QualitySettings.vSyncCount =
                        0; // disable vsync while converting so we don't limit conversion speed to framerate
                    _startTime = DateTime.Now;
                    _state = State.Converting;
                }

                break;
            case State.Converting:
                GUI.Box(new Rect(10, 10, 250, 70), "");
                GUI.Label(new Rect(15, 10, 240, 25),
                    string.Format("Converted brick {0} ({1} of {2})", _brickId, _currentFile, _inputFiles.Length));
                GUI.Label(new Rect(15, 30, 240, 25),
                    string.Format("Time: {0:D2}:{1:D2}:{2:D2}", _timeSpan.Hours, _timeSpan.Minutes, _timeSpan.Seconds));

                var nextBrickId = Path.GetFileNameWithoutExtension(_inputFiles[_currentFile] + 1);
                GUI.Label(new Rect(15, 50, 240, 25), string.Format("Now converting brick {0}...", nextBrickId));
                break;
            case State.Done:
                GUI.Box(new Rect(10, 10, 250, 70), "");
                GUI.Label(new Rect(15, 10, 240, 25),
                    string.Format("Converted brick {0} ({1} of {2})", _brickId, _currentFile, _inputFiles.Length));
                GUI.Label(new Rect(15, 30, 240, 25),
                    string.Format("Time: {0:D2}:{1:D2}:{2:D2}", _timeSpan.Hours, _timeSpan.Minutes, _timeSpan.Seconds));

                if (GUI.Button(new Rect(15, 50, 240, 25), "Back")) SceneManager.LoadScene("Scene");

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void Update()
    {
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("Clearing PlayerPrefs");
            PlayerPrefs.DeleteAll();
        }

        if (_state != State.Converting || _currentFile >= _inputFiles.Length) return;
        // RESET FROM LAST FRAME

        // nuke the bricks in the scene
        // dunno if destroying the meshes on their meshfilters is redundant with destroying the ones in the meshes list but whatever
        _meshGameObjects.ForEach(m =>
        {
            Destroy(m.GetComponent<MeshFilter>().mesh);
            Destroy(m);
        });

        _meshGameObjects.Clear();

        // shrug
        _meshes.ForEach(Destroy);
        _meshes.Clear();

        // pls
        Resources.UnloadUnusedAssets();
        GC.Collect();

        // and reset the other stuff
        _brickId = Path.GetFileNameWithoutExtension(_inputFiles[_currentFile]);
        _brickHasUVs = false;

        // LOAD MESHES FOR THIS FRAME'S BRICK

        //Debug.Log("Loading " + brickID + ", brick " + (currentFile + 1) + " of " + inputFiles.Length);
        for (var i = 0; i < 100; i++)
            if (i == 0)
            {
                _meshes.Add(LoadMesh(_inputFiles[_currentFile]));
            }
            else
            {
                if (File.Exists(_inputFiles[_currentFile] + i))
                    _meshes.Add(LoadMesh(_inputFiles[_currentFile] + i));
                else
                    break;
            }

        // SHOW THE MESHES IN THE SCENE, EXPORT, DONE
        _meshes.ForEach(m => _meshGameObjects.Add(PlopMeshIntoScene(m)));

        Export();
        _currentFile++;
        _timeSpan = DateTime.Now - _startTime;
        if (_currentFile != _inputFiles.Length) return;
        QualitySettings.vSyncCount = 1; // re-enable vsync
        _state = State.Done;
    }

    private GameObject PlopMeshIntoScene(Mesh mesh)
    {
        var newGameObject = new GameObject();
        newGameObject.transform.localScale = new Vector3(-1.0f, 1.0f, 1.0f);
        var meshFilter = newGameObject.AddComponent<MeshFilter>();
        var meshRenderer = newGameObject.AddComponent<MeshRenderer>();
        meshRenderer.material = _brickMaterial;
        meshFilter.mesh = mesh;
        return newGameObject;
    }

    private Mesh LoadMesh(string filePath)
    {
        var fileStream = new FileStream(filePath, FileMode.Open);
        var binaryReader = new BinaryReader(fileStream);

        binaryReader.BaseStream.Position = 4; // skip header
        var vertexCount = binaryReader.ReadUInt32();
        var indexCount = binaryReader.ReadUInt32();
        var flags = (Flags) binaryReader.ReadUInt32();
        //Debug.Log(Path.GetFileName(filePath) + " flags: " + flags);

        // vertices
        var vertices = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
            vertices[i] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());

        // normals
        var normals = new Vector3[vertexCount];
        if ((flags & Flags.Normals) == Flags.Normals)
        {
            for (var i = 0; i < vertexCount; i++)
                normals[i] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(),
                    binaryReader.ReadSingle());
        }
        // dummy normals, not aware of any g files that lack normals but it's theoretically possible
        else
        {
            Debug.LogWarning(Path.GetFileName(filePath) + " has no normals");
            for (var i = 0; i < vertexCount; i++) normals[i] = Vector3.up;
        }

        // uv
        var uv = new Vector2[vertexCount];
        if ((flags & Flags.UV) == Flags.UV)
        {
            _brickHasUVs = true;
            for (var i = 0; i < vertexCount; i++)
                uv[i] = new Vector2(binaryReader.ReadSingle(), -binaryReader.ReadSingle() + 1.0f);
        }
        // set UVs to zero if mesh lacks them (will only be used if other meshes for the brick *do* use UVs)
        else
        {
            for (var i = 0; i < vertexCount; i++) uv[i] = Vector2.zero;
        }

        // triangles
        var triangles = new int[indexCount];
        for (var i = 0; i < indexCount; i++) triangles[i] = (int) binaryReader.ReadUInt32();

        // done reading
        binaryReader.Close();
        // according to some old stackoverflow post just closing the reader should (might) be enough, but just in case
        fileStream.Close();

        // return mesh
        var mesh = new Mesh
        {
            indexFormat = IndexFormat.UInt32,
            vertices = vertices,
            normals = normals,
            uv = uv,
            triangles = triangles
        };
        // for any super huge meshes with more than 65535 verts (baseplates and such)
        return mesh;
    }

    /*
        obj related code modified from the 3DXML project
        which was modified from this:
        http://wiki.unity3d.com/index.php?title=ExportOBJ
        which was modified from THIS:
        http://wiki.unity3d.com/index.php?title=ObjExporter
        oh yeah and the above g loading code is revised from something Simon (LU fan server dev guy) threw together once upon a time
        also used some documentation on the format from lcdr (or whoever else contributed to those docs) to grab the UV coords
    */

    private void Export()
    {
        _startIndex = 0;
        var meshString = new StringBuilder();
        switch (_conversionMode)
        {
            case ConversionModes.Default:
                for (var i = 0; i < _meshes.Count; i++)
                {
                    meshString.Append("\ng ").Append("g" + i).Append("\n");
                    meshString.Append(MeshToString(_meshes[i], _brickHasUVs));
                }

                break;
            case ConversionModes.NoUVs:
                for (var i = 0; i < _meshes.Count; i++)
                {
                    meshString.Append("\ng ").Append("g" + i).Append("\n");
                    meshString.Append(MeshToString(_meshes[i], false));
                }

                break;
            case ConversionModes.NoUVsAndNoGroups:
                meshString.Append(AllMeshesToString());
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        File.WriteAllText(_outputDirectory + Path.DirectorySeparatorChar + _brickId + ".obj", meshString.ToString());
        //Debug.Log("Saved file " + brickID + ".obj");
    }

    private string MeshToString(Mesh m, bool includeUVs)
    {
        var sb = new StringBuilder();

        m.vertices.ToList().ForEach(v => sb.Append(string.Format("v {0} {1} {2}\n", v.x, v.y, v.z)));

        sb.Append('\n');

        m.vertices.ToList().ForEach(v => sb.Append(string.Format("vn {0} {1} {2}\n", v.x, v.y, v.z)));

        sb.Append('\n');
        if (includeUVs)
        {
            foreach (Vector3 v in m.uv) sb.Append(string.Format("vt {0} {1}\n", v.x, v.y));

            sb.Append('\n');
        }

        if (includeUVs)
            for (var i = 0; i < m.triangles.Length; i += 3)
                sb.Append(string.Format("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n",
                    m.triangles[i] + 1 + _startIndex, m.triangles[i + 1] + 1 + _startIndex,
                    m.triangles[i + 2] + 1 + _startIndex));
        else
            for (var i = 0; i < m.triangles.Length; i += 3)
                sb.Append(string.Format("f {0}//{0} {1}//{1} {2}//{2}\n",
                    m.triangles[i] + 1 + _startIndex, m.triangles[i + 1] + 1 + _startIndex,
                    m.triangles[i + 2] + 1 + _startIndex));

        _startIndex += m.vertices.Length;
        return sb.ToString();
    }

    private string AllMeshesToString()
    {
        var sb = new StringBuilder();

        _meshes.ForEach(m =>
        {
            m.vertices.ToList().ForEach(v => sb.Append(string.Format("v {0} {1} {2}\n", v.x, v.y, v.z)));
        });

        sb.Append("\n");

        _meshes.ForEach(m =>
            m.normals.ToList().ForEach(v => sb.Append(string.Format("vn {0} {1} {2}\n", v.x, v.y, v.z))));

        sb.Append("\n");

        _meshes.ForEach(m =>
        {
            for (var i = 0; i < m.triangles.Length; i += 3)
                sb.Append(string.Format("f {0}//{0} {1}//{1} {2}//{2}\n",
                    m.triangles[i] + 1 + _startIndex, m.triangles[i + 1] + 1 + _startIndex,
                    m.triangles[i + 2] + 1 + _startIndex));

            _startIndex += m.vertices.Length;
        });

        return sb.ToString();
    }

    // not using but could be useful if we do duplicate vertex merging at some point (existing code for that from the 3DXML project doesn't like it)
//#define COMBINE_MESHES
#if COMBINE_MESHES
    private Mesh CombineMeshes()
    {
        var blahStartIndex = 0;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        _meshes.ForEach(m =>
        {
            vertices.AddRange(m.vertices);
            normals.AddRange(m.normals);
            triangles.AddRange(m.triangles.Select(t => t + blahStartIndex));
            blahStartIndex += m.vertices.Length;
        });

        var mesh = new Mesh
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            vertices = vertices.ToArray(),
            normals = normals.ToArray(),
            triangles = triangles.ToArray()
        };
        // for any super huge meshes with more than 65535 verts (baseplates and such)
        return mesh;
    }
#endif
}