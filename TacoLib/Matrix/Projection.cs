using System.Numerics;

namespace TacoLib.Matrix
{
        public struct TranslatedPoint
        {
            public bool Visible;
            public Vector2 normalizedPos;
            public Vector2 pos;
            public Vector4 relativePoint;
            public float Distance;
        }

        public class Projection
        {
            public int Height;
            public int Width;

            public Vector2 HW { get; set; }

            public float FoV;
            public float zNear;
            public float zFar;
            Vector3 cameraPosition;
            Vector3 cameraDirection;

            private Matrix4x4 projectionMatrix;
            private Matrix4x4 perspectiveMatrix;

            public void SetupProjection(int screenHeight, int screenWidth, float fov, Vector3 CameraPosition, Vector3 CameraDirection, float zNear = 0.01f, float zFar = 1000f)
            {
                Height = screenHeight;
                Width = screenWidth;
                HW = new Vector2(Width, Height);
                FoV = fov;
                cameraDirection = CameraDirection;
                cameraPosition = CameraPosition;
                this.zNear = zNear;
                this.zFar = zFar;
                GenerateMatrix();
            }

            private Vector4 MulMatrixVector(Vector3 vector, Matrix4x4 matrix)
            {
                return MulMatrixVector(new Vector4(vector, 1), matrix);
            }

            private Vector4 MulMatrixVector(Vector4 vector, Matrix4x4 matrix)
            {
                var vOut = new Vector4();
                vOut.X = vector.X * matrix.M11 + vector.Y * matrix.M21 + vector.Z * matrix.M31 + vector.W * matrix.M41;
                vOut.Y = vector.X * matrix.M12 + vector.Y * matrix.M22 + vector.Z * matrix.M32 + vector.W * matrix.M42;
                vOut.Z = vector.X * matrix.M13 + vector.Y * matrix.M23 + vector.Z * matrix.M33 + vector.W * matrix.M43;
                vOut.W = vector.X * matrix.M14 + vector.Y * matrix.M24 + vector.Z * matrix.M34 + vector.W * matrix.M44;
                return vOut;
            }

            public TranslatedPoint DoPoint(Vector3 point)
            {
                var data2 = MulMatrixVector(point, projectionMatrix);

                var data = MulMatrixVector(data2, perspectiveMatrix);
                var np = new Vector2(data.X / data.W, (data.Y / data.W)) * -1 * new Vector2(0.55f, 1);
                return new TranslatedPoint()
                {
                    // fixme
                    Visible = data2.Z < 0 && (
                        np.X > -1 &&
                        np.X < 1 &&
                        np.Y > -1 &&
                        np.Y < 1
                    ),
                    relativePoint = data2,
                    normalizedPos = np,
                    Distance = Vector3.Distance(point, cameraPosition),
                    pos = ((np * 0.5f) + new Vector2(0.5f, 0.5f)) * HW
                };
            }

            private void GenerateMatrix()
            {
                projectionMatrix = Matrix4x4.CreateLookAt(cameraPosition, cameraPosition + cameraDirection, new Vector3(0, 1, 0));
                perspectiveMatrix = Matrix4x4.CreatePerspectiveFieldOfView(FoV, Width / Height, zNear, zFar);
            }
        }
    }


