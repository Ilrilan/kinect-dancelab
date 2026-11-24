using System;
using DanceLab.Core.Canonical;
using Xunit;

namespace DanceLab.Core.Tests
{
    /// <summary>Восстановление мировых осей торса из формата версии 2 (S39): по углам lean/roll и по torso.basis.</summary>
    public class TorsoFrameTests
    {
        /// <summary>Кадр с восемью валидными костями вниз и заданным торсом; новые параметры передаются по имени.</summary>
        private static CanonicalFrame Pose(double leanRad, double rollRad, double headingRad, double[]? basis = null)
        {
            var bones = new double[24];
            var valid = new bool[8];
            for (var i = 0; i < 8; i++)
            {
                bones[(i * 3) + 1] = -1;
                valid[i] = true;
            }

            return new CanonicalFrame(0, bones, valid, leanRad, 0, 0, 0, 0, 0.9, headingRad, torsoRollRad: rollRad, torsoBasis: basis);
        }

        private static void AssertAxis(Vec3 v, double x, double y, double z)
        {
            Assert.Equal(x, v.X, 9);
            Assert.Equal(y, v.Y, 9);
            Assert.Equal(z, v.Z, 9);
        }

        private static Vec3 HeadingFwd(double h) => new Vec3(Math.Sin(h), 0, Math.Cos(h));

        private static readonly double[] LeansDeg = { -170, -90, -30, 0, 45, 100, 170 };

        private static readonly double[] RollsDeg = { -80, -30, 0, 30, 80 };

        private static readonly double[] Headings = { 0, 1, 3, -2 };

        /// <summary>Вертикальный торс: Up = Y, Left = (cos h, 0, −sin h), Forward = (sin h, 0, cos h).</summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(0.7)]
        [InlineData(3.0)]
        [InlineData(-2.1)]
        public void VerticalTorsoAxesFollowHeading(double h)
        {
            var frame = Pose(0, 0, h);
            Assert.False(frame.HasTorsoBasis);
            Assert.Null(frame.TorsoBasis);
            Assert.Equal(0.0, frame.TorsoRollRad, 9);

            var tf = frame.TorsoFrame();
            AssertAxis(tf.Up, 0, 1, 0);
            AssertAxis(tf.Left, Math.Cos(h), 0, -Math.Sin(h));
            AssertAxis(tf.Forward, Math.Sin(h), 0, Math.Cos(h));
        }

        [Fact]
        public void LeanForward90TurnsUpToFloorForward()
        {
            var tf = Pose(Math.PI / 2, 0, 0).TorsoFrame();
            AssertAxis(tf.Up, 0, 0, 1);
            AssertAxis(tf.Forward, 0, -1, 0);
        }

        [Fact]
        public void LeanBack90TurnsUpToFloorBackward()
        {
            var tf = Pose(-Math.PI / 2, 0, 0).TorsoFrame();
            AssertAxis(tf.Up, 0, 0, -1);
            AssertAxis(tf.Forward, 0, 1, 0);
        }

        [Fact]
        public void Roll90TiltsUpAndLeft()
        {
            var tf = Pose(0, Math.PI / 2, 0).TorsoFrame();
            AssertAxis(tf.Up, -1, 0, 0);
            AssertAxis(tf.Left, 0, 1, 0);
        }

        /// <summary>Обратный ход по углам: lean и roll восстанавливаются из осей по формулам S39 с точностью 1e-9.</summary>
        [Fact]
        public void AnglesAreRecoveredFromTorsoAxes()
        {
            var y = new Vec3(0, 1, 0);
            foreach (var leanDeg in LeansDeg)
            {
                foreach (var rollDeg in RollsDeg)
                {
                    foreach (var h in Headings)
                    {
                        var lean = leanDeg * Math.PI / 180.0;
                        var roll = rollDeg * Math.PI / 180.0;
                        var tf = Pose(lean, roll, h).TorsoFrame();
                        var fwd = HeadingFwd(h);
                        var l = Vec3.Cross(y, fwd);
                        var lean2 = Math.Atan2(Vec3.Dot(tf.Up, fwd), Vec3.Dot(tf.Up, y));
                        var u1 = (y * Math.Cos(lean2)) + (fwd * Math.Sin(lean2));
                        var roll2 = Math.Atan2(-Vec3.Dot(tf.Up, l), Vec3.Dot(tf.Up, u1));
                        Assert.True(Math.Abs(lean2 - lean) < 1e-9, $"lean {leanDeg}°, roll {rollDeg}°, h {h}: lean2={lean2}");
                        Assert.True(Math.Abs(roll2 - roll) < 1e-9, $"lean {leanDeg}°, roll {rollDeg}°, h {h}: roll2={roll2}");
                    }
                }
            }
        }

        /// <summary>torso.basis: оси, записанные в координатах (L, Y, F) кадра, восстанавливаются теми же с точностью 1e-9; lean/roll при этом игнорируются, heading сохраняется.</summary>
        [Fact]
        public void BasisRoundTripGivesSameAxes()
        {
            var y = new Vec3(0, 1, 0);
            foreach (var leanDeg in LeansDeg)
            {
                foreach (var rollDeg in RollsDeg)
                {
                    foreach (var h in Headings)
                    {
                        var lean = leanDeg * Math.PI / 180.0;
                        var roll = rollDeg * Math.PI / 180.0;
                        var byAngles = Pose(lean, roll, h).TorsoFrame();
                        var fwd = HeadingFwd(h);
                        var l = Vec3.Cross(y, fwd);
                        var basis = new[]
                        {
                            Vec3.Dot(byAngles.Left, l), Vec3.Dot(byAngles.Left, y), Vec3.Dot(byAngles.Left, fwd),
                            Vec3.Dot(byAngles.Up, l), Vec3.Dot(byAngles.Up, y), Vec3.Dot(byAngles.Up, fwd),
                            Vec3.Dot(byAngles.Forward, l), Vec3.Dot(byAngles.Forward, y), Vec3.Dot(byAngles.Forward, fwd),
                        };
                        var withBasis = Pose(0, 0, h, basis);
                        Assert.True(withBasis.HasTorsoBasis);
                        Assert.NotNull(withBasis.TorsoBasis);

                        var tf = withBasis.TorsoFrame();
                        AssertAxis(tf.Left, byAngles.Left.X, byAngles.Left.Y, byAngles.Left.Z);
                        AssertAxis(tf.Up, byAngles.Up.X, byAngles.Up.Y, byAngles.Up.Z);
                        AssertAxis(tf.Forward, byAngles.Forward.X, byAngles.Forward.Y, byAngles.Forward.Z);
                    }
                }
            }
        }

        /// <summary>torso.basis копируется конструктором: правка входного массива кадр не меняет.</summary>
        [Fact]
        public void BasisIsCopiedFromInput()
        {
            var basis = new[] { 1.0, 0, 0, 0, 1, 0, 0, 0, 1 };
            var frame = Pose(0, 0, 0, basis);
            basis[0] = 5;
            Assert.Equal(1.0, frame.TorsoBasis![0], 9);
            var tf = frame.TorsoFrame();
            AssertAxis(tf.Left, 1, 0, 0);
            AssertAxis(tf.Up, 0, 1, 0);
            AssertAxis(tf.Forward, 0, 0, 1);
        }

        [Fact]
        public void BasisOfWrongLengthIsRejected()
        {
            var bones = new double[24];
            var valid = new bool[8];
            Assert.Throws<ArgumentException>(() => new CanonicalFrame(0, bones, valid, 0, 0, 0, 0, 0, 0.9, 0, torsoBasis: new double[8]));
        }
    }
}
