using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Numerics;

namespace trayracer
{
    public class RayTracer
    {
        
        private const int MaxDepth = 10;
        private readonly int screenHeight;

        private readonly int screenWidth;
        public RayTracer(int screenWidth, int screenHeight)
        {
            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
        }

        private IEnumerable<ISect> Intersections(Ray ray, Scene scene)
        {
            return scene.Things
                .Select(obj => obj.Intersect(ray))
                .Where(inter => inter != null)
                .OrderBy(inter => inter.Dist);
        }

        private float TestRay(Ray ray, Scene scene)
        {
            var isects = Intersections(ray, scene);
            var isect = isects.FirstOrDefault();
            if (isect == null)
                return 0;
            return isect.Dist;
        }

        private Vector3 TraceRay(Ray ray, Scene scene, int depth)
        {
            var isects = Intersections(ray, scene);
            var isect = isects.FirstOrDefault();
            if (isect == null)
                return new Vector3(0,0,0);
            return Shade(isect, scene, depth);
        }

        private Vector3 GetNaturalColor(SceneObject thing, Vector3 pos, Vector3 norm, Vector3 rd, Scene scene)
        {
            var ret = new Vector3(0, 0, 0);
            foreach (var light in scene.Lights)
            {
                var ldis = Vector3.Subtract(light.Pos, pos);
                var livec = Vector3.Normalize(ldis);
                var neatIsect = TestRay(new Ray { Start = pos, Dir = livec }, scene);
                var isInShadow = !(neatIsect > ldis.Length() || neatIsect == 0);
                if (!isInShadow)
                {
                    var illum = Vector3.Dot(livec, norm);
                    var lcolor = illum > 0 ? Vector3.Multiply(illum, light.Color) : new Vector3(0, 0, 0);
                    var specular = Vector3.Dot(livec, Vector3.Normalize(rd));
                    var scolor = specular > 0
                        ? Vector3.Multiply((float)Math.Pow(specular, thing.Surface.Roughness), light.Color)
                        : new Vector3(0, 0, 0);
                    ret = Vector3.Add(ret, Vector3.Add(Vector3.Multiply(thing.Surface.Diffuse(pos), lcolor),
                        Vector3.Multiply(thing.Surface.Specular(pos), scolor)));
                }
            }
            return ret;
        }

        private Vector3 GetReflectionColor(SceneObject thing, Vector3 pos, Vector3 norm, Vector3 rd, Scene scene, int depth)
        {
            return Vector3.Multiply((float)thing.Surface.Reflect(pos), TraceRay(new Ray { Start = pos, Dir = rd }, scene, depth + 1));
        }

        private Vector3 Shade(ISect isect, Scene scene, int depth)
        {
            var d = isect.Ray.Dir;
            var pos = Vector3.Add(Vector3.Multiply((float)isect.Dist, isect.Ray.Dir), isect.Ray.Start);
            var normal = isect.Thing.Normal(pos);
            var reflectDir = Vector3.Subtract(d, Vector3.Multiply(2 * Vector3.Dot(normal, d), normal));
            var ret = new Vector3(0,0,0);
            ret = Vector3.Add(ret, GetNaturalColor(isect.Thing, pos, normal, reflectDir, scene));
            if (depth >= MaxDepth) return Vector3.Add(ret, new Vector3(.5f, .5f, .5f));
            return Vector3.Add(ret,
                GetReflectionColor(isect.Thing, Vector3.Add(pos, Vector3.Multiply(.001f, reflectDir)), normal, reflectDir,
                    scene, depth));
        }

        private float RecenterX(float x)
        {
            return (x - screenWidth / 2.0f) / (0.9f * screenWidth);
        }

        private float RecenterY(float y)
        {
            return -(y - screenHeight / 2.0f) / (1.6f * screenHeight);
        }

        private Vector3 GetPoint(float x, float y, Camera camera)
        {
            return Vector3.Normalize(Vector3.Add(camera.Forward, Vector3.Add(Vector3.Multiply(RecenterX(x), camera.Right),
                Vector3.Multiply(RecenterY(y), camera.Up))));
        }

        Color ToDrawingColor(Vector3 col)
        {
            return Color.FromArgb((int)(Legalize(col.X) * 255), (int)(Legalize(col.Y) * 255),
                (int)(Legalize(col.Z) * 255));
        }
        public float Legalize(float d)
        {
            return d > 1 ? 1 : d;
        }

        public void Render(Scene scene, Bitmap bmp)
        {

            var rect = new Rectangle(0, 0, screenWidth, screenHeight);
            var bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);
            var ptr = bmpData.Scan0;
            var bytes = Math.Abs(bmpData.Stride) * bmp.Height;
            var rgbValues = new byte[bytes];
            Marshal.Copy(ptr, rgbValues, 0, bytes);
            
            Parallel.ForEach(SteppedIterator(0,rgbValues.Length,3), i =>
            {
                //if (i % 3 != 0) return;
                var color = TraceRay(
                    new Ray
                    {
                        Start = scene.Camera.Pos,
                        Dir = GetPoint(i / 3 % screenWidth, i / 3 / screenWidth, scene.Camera)
                    }, scene, 0);
                var c = ToDrawingColor(color);
                rgbValues[i] = c.B;
                rgbValues[i + 1] = c.G;
                rgbValues[i + 2] = c.R;
            });
            Marshal.Copy(rgbValues, 0, ptr, bytes);
            bmp.UnlockBits(bmpData);
        }
        private static IEnumerable<int> SteppedIterator(int startIndex, int endIndex, int stepSize)
        {
            for (int i = startIndex; i < endIndex; i = i + stepSize)
            {
                yield return i;
            }
        }
    }
    public static class Surfaces
    {
        // Only works with X-Z plane.
        public static readonly Surface XWall =
            new Surface
            {
                Diffuse = pos => (Math.Floor(pos.Y) + Math.Floor(pos.X)) % 2 != 0
                    ? new Vector3(1, 1, 1)
                    : new Vector3(0, 0, 0),
                Specular = pos => new Vector3(1, 1, 1),
                Reflect = pos => (Math.Floor(pos.Y) + Math.Floor(pos.X)) % 2 != 0
                    ? .7f
                    : .1f,
                Roughness = 150
            };
        public static readonly Surface ZWall =
            new Surface
            {
                Diffuse = pos => (Math.Floor(pos.Y) + Math.Floor(pos.Z)) % 2 != 0
                    ? new Vector3(1, 1, 1)
                    : new Vector3(0, 0, 0),
                Specular = pos => new Vector3(1, 1, 1),
                Reflect = pos => (Math.Floor(pos.Y) + Math.Floor(pos.Z)) % 2 != 0
                    ? .7f
                    : .1f,
                Roughness = 150
            };
        public static readonly Surface CheckerBoard =
            new Surface
            {
                Diffuse = pos => (Math.Floor(pos.Z) + Math.Floor(pos.X)) % 1 != 0
                    ? new Vector3(1, 1, 1)
                    : new Vector3(0, 0, 0),
                Specular = pos => new Vector3(1, 1, 1),
                Reflect = pos => (Math.Floor(pos.Z) + Math.Floor(pos.X)) % 1 != 0
                    ? .7f
                    : .1f,
                Roughness = 150
            };
        public static readonly Surface Boci =
            new Surface
            {
                Diffuse = pos => Math.Sin(pos.Z) + Math.Cos(pos.X) < 0.15
                    ? new Vector3(1, 1, 1)
                    : new Vector3(0, 0, 0),
                Specular = pos => new Vector3(1, 1, 1),
                Reflect = pos => Math.Sin(pos.Z) + Math.Cos(pos.X) < 0.15
                     ? .7f
                     : .1f,
                Roughness = 150
            };


        public static readonly Surface Shiny =
            new Surface
            {
                Diffuse = pos => new Vector3(1, 1, 1),
                Specular = pos => new Vector3(.5f, .5f, .5f),
                Reflect = pos => .6f,
                Roughness = 50
            };
    }
    public class Ray
    {
        public Vector3 Dir;
        public Vector3 Start;
    }
    public class ISect
    {
        public float Dist;
        public Ray Ray;
        public SceneObject Thing;
    }
    public class Surface
    {
        public Func<Vector3, Vector3> Diffuse;
        public Func<Vector3, float> Reflect;
        public float Roughness;
        public Func<Vector3, Vector3> Specular;
        
    }
    public class Camera
    {
        public Vector3 Forward;
        public Vector3 Pos;
        public Vector3 Right;
        public Vector3 Up;

        public static Camera Create(Vector3 pos, Vector3 lookAt)
        {
            var forward = Vector3.Normalize(Vector3.Subtract(lookAt, pos));
            var down = new Vector3(0, -1, 0);
            var right = Vector3.Multiply(1.5f, Vector3.Normalize(Vector3.Cross(forward, down)));
            var up = Vector3.Multiply(1.5f, Vector3.Normalize(Vector3.Cross(forward, right)));

            return new Camera { Pos = pos, Forward = forward, Up = up, Right = right };
        }
    }
    public class Light
    {
        public Vector3 Color;
        public Vector3 Pos;
    }
    public abstract class SceneObject
    {
        public Surface Surface;
        public abstract ISect Intersect(Ray ray);
        public abstract Vector3 Normal(Vector3 pos);
    }
    public class Sphere : SceneObject
    {
        public Vector3 Center;
        public float Radius;

        public override ISect Intersect(Ray ray)
        {
            var eo = Vector3.Subtract(Center, ray.Start);
            float v = Vector3.Dot(eo, ray.Dir);
            float dist;
            if (v < 0)
            {
                dist = 0;
            }
            else
            {
                var disc = (Radius * Radius) - (Vector3.Dot(eo, eo) - (v * v));
                dist = disc < 0 ? 0 : v - (float)Math.Sqrt(disc);
            }
            if (dist == 0) return null;
            return new ISect
            {
                Thing = this,
                Ray = ray,
                Dist = dist
            };
        }

        public override Vector3 Normal(Vector3 pos)
        {
            return Vector3.Normalize(Vector3.Subtract(pos, Center));
        }
    }
    public class Plane : SceneObject
    {
        public Vector3 Norm;
        public float Offset;

        public override ISect Intersect(Ray ray)
        {
            var denom = Vector3.Dot(Norm, ray.Dir);
            if (denom > 0) return null;
            return new ISect
            {
                Thing = this,
                Ray = ray,
                Dist = (Vector3.Dot(Norm, ray.Start) + Offset) / -denom
            };
        }

        public override Vector3 Normal(Vector3 pos)
        {
            return Norm;
        }
    }
    public class Scene
    {
        public Camera Camera;
        public Light[] Lights;
        public SceneObject[] Things;

        public IEnumerable<ISect> Intersect(Ray r)
        {
            return from thing in Things
                   select thing.Intersect(r);
        }
    }
    public sealed class RayTracerForm : Form
    {
        private readonly Bitmap bitmap;
        private readonly int bmheight = 720 / 1;
        private readonly int bmwidth = 1280 / 1;
        private readonly int formheight = 720 / 1;
        private readonly int formwidth = 1280 / 1;
        private readonly PictureBoxWithInterpolationMode pictureBox;
        private readonly RayTracer rayTracer;
        

        private readonly Scene scene = new Scene
        {
            Things = new SceneObject[]
            {
                new Plane
                {
                    Norm = new Vector3(0, 1, 0),
                    Offset = 0,
                    Surface = Surfaces.Boci
                },
                new Sphere
                {
                    Center=new Vector3(-3,1,0),
                    Radius= -1,
                    Surface=Surfaces.CheckerBoard
                },
                new Sphere
                {
                    Center = new Vector3(0, 1, 0),
                    Radius = 1,
                    Surface = Surfaces.Shiny
                },
                new Sphere
                {
                    Center = new Vector3(-1, .5f, 1.5f),
                    Radius = .5f,
                    Surface = Surfaces.Shiny
                },
                new Sphere
                {
                    Center = new Vector3(-8, 3, 1.75f),
                    Radius = 3,
                    Surface = Surfaces.Shiny
                }
            },
            Lights = new[]
            {
                new Light
                {
                    Pos = new Vector3(-2, 2.5f, 0),
                    Color = new Vector3(1f, 0f, 0f)
                },
                new Light
                {
                    Pos = new Vector3(1.5f, 2.5f, 1.5f),
                    Color = new Vector3(0f, 1f, 0f)
                },
                new Light
                {
                    Pos = new Vector3(1.5f, 2.5f, -1.5f),
                    Color = new Vector3(0f, 0f, 1f)
                },
                new Light
                {
                    Pos = new Vector3(0, 3.5f, 0),
                    Color = new Vector3(.5f, .5f, .5f)
                },
            },
            Camera = Camera.Create(new Vector3(3, 2, 4), new Vector3(-.5f, .5f, 0))
        };
        private readonly Scene wallScene = new Scene
        {
            Things = new SceneObject[]
            {
                new Plane
                {
                    Norm = new Vector3(1, 1, 0),
                    Offset = 15,
                    Surface = Surfaces.ZWall
                },
                new Plane
                {
                    Norm = new Vector3(-1, 1, 0),
                    Offset = 15,
                    Surface = Surfaces.ZWall
                },
                new Plane
                {
                    Norm = new Vector3(0, 1, 1),
                    Offset = 15,
                    Surface = Surfaces.XWall
                },
                new Plane
                {
                    Norm = new Vector3(0, 1, -1),
                    Offset = 15,
                    Surface = Surfaces.XWall
                },
                new Plane
                {
                    Norm = new Vector3(0, 1, 0),
                    Offset = 0,
                    Surface = Surfaces.Boci
                },
                new Sphere
                {
                    Center = new Vector3(-3,1,0),
                    Radius = -1,
                    Surface = Surfaces.CheckerBoard
                },
                new Sphere
                {
                    Center = new Vector3(0, 1, 0),
                    Radius = 1,
                    Surface = Surfaces.Shiny
                },
                new Sphere
                {
                    Center = new Vector3(-1, .5f, 1.5f),
                    Radius = .5f,
                    Surface = Surfaces.Shiny
                },
                new Sphere
                {
                    Center = new Vector3(-8, 3, 1.75f),
                    Radius = 3,
                    Surface = Surfaces.Shiny
                }
            },
            Lights = new[]
            {
                new Light
                {
                    Pos = new Vector3(-2, 2.5f, 0),
                    Color = new Vector3(.8f, .07f, .07f)
                },
                new Light
                {
                    Pos = new Vector3(1.5f, 2.5f, 1.5f),
                    Color = new Vector3(.07f, .07f, .49f)
                },
                new Light
                {
                    Pos = new Vector3(1.5f, 2.5f, -1.5f),
                    Color = new Vector3(.07f, .49f, .071f)
                },
                new Light
                {
                    Pos = new Vector3(0, 3.5f, 0),
                    Color = new Vector3(.5f, .5f, .5f)
                },
            },
            Camera = Camera.Create(new Vector3(3, 2, 4), new Vector3(-.5f, .5f, 0))
        };

        private Stopwatch sw = new Stopwatch();
        private float theta;
        private readonly Timer timer;

        public RayTracerForm()
        {
            DoubleBuffered = true;
            bitmap = new Bitmap(bmwidth, bmheight, PixelFormat.Format24bppRgb);
            rayTracer = new RayTracer(bmwidth, bmheight);
            pictureBox = new PictureBoxWithInterpolationMode
            {
                InterpolationMode = InterpolationMode.NearestNeighbor,
                Bounds = new Rectangle(0, 0, formwidth, formheight),
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image = bitmap
            };
            FormBorderStyle = FormBorderStyle.None;
            Controls.Add(pictureBox);
            Text = "Ray Tracer";
            timer = new Timer();
            timer.Tick += Timer_Tick;
            timer.Interval = 1;
            timer.Enabled = true;
            Load += RayTracerForm_Load;
            KeyDown += RayTracerForm_KeyDown;
            Show();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            var actualScene = scene;
            sw = new Stopwatch();
            sw.Start();
            actualScene.Camera = Camera.Create(new Vector3((float)Math.Sin(theta) * 5, 3, (float)Math.Cos(theta) * 5), new Vector3(0, 1, 0));
            rayTracer.Render(actualScene, bitmap);
            theta += 0.025f;
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds.ToString());
            pictureBox.Invalidate();
        }

        private void RayTracerForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.S)
            {
                bitmap.Save("rendered.png", ImageFormat.Png);
            }
        }

        private void RayTracerForm_Load(object sender, EventArgs e)
        {
            Bounds = new Rectangle(0, 0, formwidth, formheight);
            if (!timer.Enabled)
            {
                rayTracer.Render(scene, bitmap);
            }
            pictureBox.Invalidate();
        }

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();

            Application.Run(new RayTracerForm());
        }
    }
}