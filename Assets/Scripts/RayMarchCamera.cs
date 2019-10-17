using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
[ExecuteInEditMode]
public class RayMarchCamera : MonoBehaviour
{
    //TODO There is an issue with velocity only being between 0 and 1. I need to use a compute buffer instead of a render texture to fix this issue.

    [SerializeField]
    private Shader _shader;

    const int SIZE = 128;
    const int NUM_THREADS = 8;
    const float TIME_STEP = 0.1f;

    public float inputRadius = 0.04f;
    public Vector4 inputPos = new Vector4(0.5f, 0.9f, 0.5f);
    public float densityBuoyancy = 1.0f;
    public float temperature = 10.0f;
    public float temperatureDissipation = 0.995f;
    public float densityWeight = 0.0125f;
    public float velocityDissipation = 3.0f;
    public float vorticityStrength = 0;
    public int iterations = 1;
    public float dt = 0.1f;
    public float densityDissipation = 0.995f;
    public float densityAmount = 1.0f;
    public float ambientTemperature = 0.0f;

    public Material _raymarchMaterial
    {
        get 
        {
            if (!_raymarchMat && _shader)
            {
                _raymarchMat = new Material(_shader);
                _raymarchMat.hideFlags = HideFlags.HideAndDontSave;
            }
            return _raymarchMat;
        }
    }

    private Material _raymarchMat;

    public Camera _camera
    {
        get
        {
            if (!_cam)
            {
                _cam = GetComponent<Camera>();

            }

            return _cam;
        }
    }

    public RenderTexture densityWrite, velocityWrite, pressureWrite, temperatureWrite, phiWrite, temp3f, obstacles;
    public RenderTexture densityRead, velocityRead, pressureRead, temperatureRead, phiRead;

    public ComputeShader applyImpulse, applyAdvect, computeVorticity;
    public ComputeShader computeDivergence, computeJacobi, computeProjection;
    public ComputeShader computeConfinement, computeObstacles, applyBuoyancy;

    private Camera _cam;

    public Transform _directionalLight;

    public ComputeShader compute;

    public float _maxDistance;

    private bool genTex = false;

    private void Update()
    {
        advect(dt, temperatureDissipation, 0.0f, temperatureWrite, temperatureRead);
        advect(dt, densityDissipation, 0.0f, densityWrite, densityRead);

        advectVelocity(dt);

        addBouyancy(dt);

        addImpluse(dt, densityAmount, densityWrite, densityRead);
        addImpluse(dt, temperature, temperatureWrite, temperatureRead);

        //vorticityConfinement(dt);

        divergence();
        pressure();
        projection();
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (!_raymarchMaterial)
        {
            Graphics.Blit(source, destination);
        }


        _raymarchMaterial.SetVector("_lightDir", _directionalLight ? _directionalLight.forward : Vector3.down);
        _raymarchMaterial.SetMatrix("_CamFrustrum", CamFrustrum(_camera));
        _raymarchMaterial.SetMatrix("_CamToWorld", _camera.cameraToWorldMatrix);
        _raymarchMaterial.SetFloat("_maxDistance", _maxDistance);
        _raymarchMaterial.SetTexture("_NoiseTex", densityWrite);

        RenderTexture.active = destination;
        _raymarchMaterial.SetTexture("_MainTex", source);
        GL.PushMatrix();
        GL.LoadOrtho();
        _raymarchMaterial.SetPass(0);

        GL.Begin(GL.QUADS);
        //BL
        GL.MultiTexCoord2(0, 0, 0);
        GL.Vertex3(0, 0, 3);
        //BR
        GL.MultiTexCoord2(0, 1, 0);
        GL.Vertex3(1, 0, 2);
        //TR
        GL.MultiTexCoord2(0, 1, 1);
        GL.Vertex3(1, 1, 1);
        //TL
        GL.MultiTexCoord2(0, 0, 1);
        GL.Vertex3(0, 1, 0);

        GL.End();
        GL.PopMatrix();
    }

    public void Start()
    {
        densityWrite = createTexture(SIZE, RenderTextureFormat.RFloat);
        velocityWrite = createTexture(SIZE, RenderTextureFormat.ARGBFloat);
        pressureWrite = createTexture(SIZE, RenderTextureFormat.RFloat);
        temperatureWrite = createTexture(SIZE, RenderTextureFormat.RFloat);
        phiWrite = createTexture(SIZE, RenderTextureFormat.RFloat);
        temp3f = createTexture(SIZE, RenderTextureFormat.ARGBFloat);
        obstacles = createTexture(SIZE, RenderTextureFormat.RInt);

        densityRead = createTexture(SIZE, RenderTextureFormat.RFloat);
        velocityRead = createTexture(SIZE, RenderTextureFormat.ARGBFloat);
        pressureRead = createTexture(SIZE, RenderTextureFormat.RFloat);
        temperatureRead = createTexture(SIZE, RenderTextureFormat.RFloat);
        phiRead = createTexture(SIZE, RenderTextureFormat.RFloat);

        setObstacles();
    }

    void copyWrite(RenderTexture src, RenderTexture dst)
    {
        RenderTexture tmp = dst;
        dst = src;
        src = tmp;
    }

    public void setObstacles()
    {
        computeObstacles.SetVector("Size", new Vector4(SIZE, SIZE, SIZE, SIZE));
        computeObstacles.SetTexture(0, "Write", obstacles);
        computeObstacles.Dispatch(0, SIZE / NUM_THREADS, SIZE / NUM_THREADS, SIZE / NUM_THREADS);
    }

    public void addImpluse(float dt, float amount, RenderTexture write, RenderTexture read)
    {
        applyImpulse.SetVector("Size", new Vector4(SIZE, SIZE, SIZE, SIZE));
        applyImpulse.SetFloat("Radius", inputRadius);
        applyImpulse.SetFloat("Amount", amount);
        applyImpulse.SetFloat("DeltaTime", dt);
        applyImpulse.SetVector("Pos", inputPos);
        applyImpulse.SetTexture(0, "Write", write);
        applyImpulse.SetTexture(0, "Read", read);

        applyImpulse.Dispatch(0, SIZE / NUM_THREADS, SIZE / NUM_THREADS, SIZE / NUM_THREADS);

        copyWrite(write, read);
    }

    public void addBouyancy(float dt)
    {
        applyBuoyancy.SetVector("Size", new Vector4(SIZE, SIZE, SIZE, SIZE));
        applyBuoyancy.SetVector("Up", new Vector4(0, 1, 0, 0));
        applyBuoyancy.SetFloat("Buoyancy", densityBuoyancy);
        applyBuoyancy.SetFloat("AmbientTemp", ambientTemperature);
        applyBuoyancy.SetFloat("Weight", densityWeight);
        applyBuoyancy.SetFloat("DeltaTime", dt);

        applyBuoyancy.SetTexture(0, "VelocityWrite", velocityWrite);
        applyBuoyancy.SetTexture(0, "VelocityRead", velocityRead);
        applyBuoyancy.SetTexture(0, "Density", densityRead);
        applyBuoyancy.SetTexture(0, "Temperature", temperatureRead);

        applyBuoyancy.Dispatch(0, SIZE / NUM_THREADS, SIZE / NUM_THREADS, SIZE / NUM_THREADS);

        copyWrite(velocityWrite, velocityRead);
    }

    public void advect(float dt, float dissipation, float decay, RenderTexture write, RenderTexture read, float foward = 1.0f)
    {
        applyAdvect.SetVector("Size", new Vector4(SIZE, SIZE, SIZE, SIZE));
        applyAdvect.SetFloat("DeltaTime", dt);
        applyAdvect.SetFloat("Dissipate", dissipation);
        applyAdvect.SetFloat("Forward", foward);
        applyAdvect.SetFloat("Decay", decay);

        applyAdvect.SetTexture(0, "Read1f", read);  
        applyAdvect.SetTexture(0, "Write1f", write);
        applyAdvect.SetTexture(0, "VelocityRead", velocityRead);
        applyAdvect.SetTexture(0, "Obstacles", obstacles);

        applyAdvect.Dispatch(0, SIZE / NUM_THREADS, SIZE / NUM_THREADS, SIZE / NUM_THREADS);

        copyWrite(write, read);
    }

    public void advectVelocity(float dt) 
    {
        applyAdvect.SetVector("Size", new Vector4(SIZE, SIZE, SIZE, SIZE));
        applyAdvect.SetFloat("DeltaTime", dt);
        applyAdvect.SetFloat("Dissipate", velocityDissipation);
        applyAdvect.SetFloat("Forward", 1.0f);
        applyAdvect.SetFloat("Decay", 0.0f);

        applyAdvect.SetTexture(1, "Read3f", velocityRead);
        applyAdvect.SetTexture(1, "Write3f", velocityWrite);
        applyAdvect.SetTexture(1, "VelocityRead", velocityRead);
        applyAdvect.SetTexture(1, "Obstacles", obstacles); 

        applyAdvect.Dispatch(1, SIZE / NUM_THREADS, SIZE / NUM_THREADS, SIZE / NUM_THREADS);

        copyWrite(velocityWrite, velocityRead);
    }

    public void vorticityConfinement(float dt)
    {
        computeVorticity.SetVector("Size", new Vector4(SIZE, SIZE, SIZE, SIZE));
        computeVorticity.SetTexture(0, "VelocityWrite", temp3f);
        computeVorticity.SetTexture(0, "VelocityRead", velocityRead);

        computeVorticity.Dispatch(0, SIZE / NUM_THREADS, SIZE / NUM_THREADS, SIZE / NUM_THREADS);

        computeConfinement.SetVector("Size", new Vector4(SIZE, SIZE, SIZE, SIZE));
        computeConfinement.SetFloat("DeltaTime", dt);
        computeConfinement.SetFloat("Epsilon", vorticityStrength);

        computeConfinement.SetTexture(0, "VelocityWrite", velocityWrite);
        computeConfinement.SetTexture(0, "VelocityRead", velocityRead);
        computeConfinement.SetTexture(0, "Vorticity", temp3f);

        computeConfinement.Dispatch(0, SIZE / NUM_THREADS, SIZE / NUM_THREADS, SIZE / NUM_THREADS);

        copyWrite(velocityWrite, velocityRead);
    }

    public void divergence()
    {
        computeDivergence.SetVector("Size", new Vector4(SIZE, SIZE, SIZE, SIZE));

        computeDivergence.SetTexture(0, "Temp3f", temp3f);
        computeDivergence.SetTexture(0, "VelocityRead", velocityRead);
        computeDivergence.SetTexture(0, "Obstacles", obstacles);

        computeDivergence.Dispatch(0, SIZE / NUM_THREADS, SIZE / NUM_THREADS, SIZE / NUM_THREADS);
    }

    public void pressure()
    {
        computeJacobi.SetVector("Size", new Vector4(SIZE, SIZE, SIZE, SIZE));
        computeJacobi.SetTexture(0, "Divergence", temp3f);
        computeJacobi.SetTexture(0, "Obstacles", obstacles);

        for (int i = 0; i < iterations; i++)
        {
            computeJacobi.SetTexture(0, "PressureWrite", pressureWrite);
            computeJacobi.SetTexture(0, "PressureRead", pressureRead);

            computeJacobi.Dispatch(0, SIZE / NUM_THREADS, SIZE / NUM_THREADS, SIZE / NUM_THREADS);

            copyWrite(pressureWrite, pressureRead);
        }

        copyWrite(pressureWrite, pressureRead);
    }

    void projection()
    {
        computeProjection.SetVector("Size", new Vector4(SIZE, SIZE, SIZE, SIZE));
        computeProjection.SetTexture(0, "Obstacles", obstacles);
        computeProjection.SetTexture(0, "PressureRead", pressureRead);
        computeProjection.SetTexture(0, "VelocityRead", velocityRead);
        computeProjection.SetTexture(0, "VelocityWrite", velocityWrite);

        computeProjection.Dispatch(0, SIZE / NUM_THREADS, SIZE / NUM_THREADS, SIZE / NUM_THREADS);

        copyWrite(velocityWrite, velocityRead);
    }

    private Matrix4x4 CamFrustrum(Camera cam)
    {
        Matrix4x4 frustrum = Matrix4x4.identity;
        float fov = Mathf.Tan((cam.fieldOfView / 2) * Mathf.Deg2Rad);

        Vector3 goUp = Vector3.up * fov;
        Vector3 goRight = Vector3.right * fov * cam.aspect;

        Vector3 tl = -Vector3.forward - goRight + goUp;
        Vector3 tr = -Vector3.forward + goRight + goUp;
        Vector3 bl = -Vector3.forward - goRight - goUp;
        Vector3 br = -Vector3.forward + goRight - goUp;

        frustrum.SetRow(0, tl);
        frustrum.SetRow(1, tr);
        frustrum.SetRow(2, br);
        frustrum.SetRow(3, bl);

        return frustrum;
    }

    public void updateDensity()
    {

    }

    public RenderTexture createTexture(int size, RenderTextureFormat format)
    {
        RenderTexture result = new RenderTexture(size, size, 0, format, 10);
        result.enableRandomWrite = true;
        result.volumeDepth = size;
        result.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        result.format = format;
        result.Create();

        return result;
    }


}
