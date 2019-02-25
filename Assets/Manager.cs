using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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
    Uv = 1, // 0x01
    Normals = 2, // 0x02
    Flexible = 4, // 0x08
    Unknown8 = 8, // 0x04
    Unknown16 = 16, // 0x10
    Unknown32 = 32 // 0x20
}

/// <inheritdoc />
/// <summary>
///     Main .g to OBJ behavior.
/// </summary>
public class Manager : MonoBehaviour
{
    public static ConversionModes ConversionMode;

    // user input
    public static string InputDirectory = @"C:\Some\Directory";
    public static string OutputDirectory = @"C:\Another\Directory";
    public static bool ToG;
    private string _brickId;
    private int _currentFile;
    private int _garbageCounter;
    private string[] _inputFiles;
    private MeshFilter _meshFilter;
    private int _startIndex;
    private DateTime _startTime;

    // general
    private State _state;

    private TimeSpan _timeSpan;

    // inspector
    public Material BrickMaterial;

    private void Start()
    {
        if (PlayerPrefs.HasKey("Input Directory")) InputDirectory = PlayerPrefs.GetString("Input Directory");

        if (PlayerPrefs.HasKey("Output Directory")) OutputDirectory = PlayerPrefs.GetString("Output Directory");

        if (PlayerPrefs.HasKey("Conversion Mode"))
            ConversionMode = (ConversionModes) PlayerPrefs.GetInt("Conversion Mode");

        var newGameObject = new GameObject();
        newGameObject.transform.localScale = new Vector3(-1.0f, 1.0f, 1.0f);
        _meshFilter = newGameObject.AddComponent<MeshFilter>();
        newGameObject.AddComponent<MeshRenderer>().material = BrickMaterial;
    }

    private void OnGUI()
    {
        switch (_state)
        {
            case State.Start:
                GUI.Box(new Rect(10, 10, 250, 250), "");

                GUI.Label(new Rect(15, 10, 240, 25), "Input directory:");
                InputDirectory = GUI.TextField(new Rect(15, 30, 240, 25), InputDirectory, 100);

                GUI.Label(new Rect(15, 60, 240, 25), "Output directory:");
                OutputDirectory = GUI.TextField(new Rect(15, 80, 240, 25), OutputDirectory, 100);

                GUI.Label(new Rect(15, 110, 240, 25), "Conversion mode:");
                var conversionModeOptions = new[] {" Default", " No UVs", " No UVs, no groups"};
                ConversionMode = (ConversionModes) GUI.SelectionGrid(new Rect(15, 130, 240, 62), (int) ConversionMode,
                    conversionModeOptions, 1, "toggle");

                //Toggle between convening to G or OBJ
                if (GUI.Button(new Rect(15, 200, 240, 25), ToG ? "Converting To G" : "Converting To OBJ"))
                    ToG = !ToG;

                if (GUI.Button(new Rect(15, 230, 240, 25), "Go"))
                {
                    if (!Directory.Exists(InputDirectory))
                    {
                        InputDirectory = "Directory doesn't exist";
                        break;
                    }

                    _inputFiles = Directory.GetFiles(InputDirectory, ToG ? "*.obj" : "*.g",
                        SearchOption.AllDirectories);
                    //Debug.Log(inputFiles.Length + " bricks");
                    if (!_inputFiles.Any())
                    {
                        InputDirectory = $"No {(ToG ? "*.obj" : "*.g")} files found";
                        break;
                    }

                    if (!Directory.Exists(OutputDirectory)) Directory.CreateDirectory(OutputDirectory);

                    PlayerPrefs.SetString("Input Directory", InputDirectory);
                    PlayerPrefs.SetString("Output Directory", OutputDirectory);
                    PlayerPrefs.SetInt("Conversion Mode", (int) ConversionMode);
                    _garbageCounter = 0;
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
                    string.Format("Time: {0:D2}:{1:D2}", _timeSpan.Minutes, _timeSpan.Seconds));

                var nextBrickId = Path.GetFileNameWithoutExtension(_inputFiles[_currentFile] + 1);
                GUI.Label(new Rect(15, 50, 240, 25), string.Format("Now converting brick {0}...", nextBrickId));
                break;
            case State.Done:
                GUI.Box(new Rect(10, 10, 250, 70), "");
                GUI.Label(new Rect(15, 10, 240, 25),
                    string.Format("Converted brick {0} ({1} of {2})", _brickId, _currentFile, _inputFiles.Length));
                GUI.Label(new Rect(15, 30, 240, 25),
                    string.Format("Time: {0:D2}:{1:D2}", _timeSpan.Minutes, _timeSpan.Seconds));

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
        Destroy(_meshFilter.mesh);

        _garbageCounter++;
        if (_garbageCounter == 60) // arbitrary number that seems to work fine
        {
            Resources.UnloadUnusedAssets();
            GC.Collect();
            _garbageCounter = 0;
        }

        // and reset the other stuff
        _brickId = Path.GetFileNameWithoutExtension(_inputFiles[_currentFile]);

        // LOAD MESHES FOR THIS FRAME'S BRICK

        //Debug.Log("Loading " + brickID + ", brick " + (currentFile + 1) + " of " + inputFiles.Length);
        var files = new List<string>();
        for (var i = 0; i < 100; i++)
            if (i == 0)
            {
                files.Add(_inputFiles[_currentFile]);
                if (ToG)
                    break;
            }
            else
            {
                if (File.Exists(_inputFiles[_currentFile] + i))
                    files.Add(_inputFiles[_currentFile] + i);
            }

        var data = new MeshData(files);

        // SHOW THE MESHES IN THE SCENE, EXPORT, DONE

        if (ToG)
        {
            data.ExportToG();
        }
        else
        {
            foreach (var mesh in data.Meshes) PlopMeshIntoScene(mesh);
            data.ExportToObj();
        }

        _currentFile++;
        _timeSpan = DateTime.Now - _startTime;
        if (_currentFile != _inputFiles.Length) return;
        QualitySettings.vSyncCount = 1; // re-enable vsync
        _state = State.Done;
    }

    /// <summary>
    ///     Load mesh data into the scene.
    /// </summary>
    /// <param name="meshData">Mesh Data</param>
    private void PlopMeshIntoScene(MeshData.Mesh meshData)
    {
        _meshFilter.mesh = new Mesh
        {
            indexFormat = IndexFormat.UInt32,
            vertices = meshData.Vertices,
            normals = meshData.Normals,
            uv = meshData.Uv,
            triangles = meshData.Triangles
        };
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

    // not using but could be useful if we do duplicate vertex merging at some point (existing code for that from the 3DXML project doesn't like it)
    /*
    CustomMesh CombineMeshes()
    {
        int blahStartIndex = 0;
        
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();
        
        foreach (CustomMesh m in meshes)
        {
            foreach(Vector3 v in m.vertices)
            {
                vertices.Add(v);
            }
        }
        
        foreach (CustomMesh m in meshes)
        {
            foreach(Vector3 v in m.normals)
            {
                normals.Add(v);
            }
        }
        
        foreach (CustomMesh m in meshes)
        {
            for (int i = 0; i < m.triangles.Length; i++)
            {
                triangles.Add(m.triangles[i] + blahStartIndex);
            }
            blahStartIndex += m.vertices.Length;
        }
        
        CustomMesh mesh = new CustomMesh();
        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        mesh.triangles = triangles.ToArray();
        return mesh;
    }
    */
}