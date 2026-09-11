using System.Text.Json;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Procedurally builds a self-contained glTF (JSON + embedded base64 buffer) for a
/// round-brilliant-cut diamond — table, star/bezel crown bands, a faceted girdle,
/// and a pavilion fan to the culet, in 8-fold symmetry with alternating ring
/// rotation (the standard "antiprism" trick for faceted gem meshes). Every
/// triangle gets its own vertices (flat shading, no shared normals across
/// facets) and a PBR material with real transmission/IOR for viewers that
/// support those glTF extensions. Demo content for <see cref="MarketplaceSeeder"/>'s
/// 3D-model showcase — not tied to any real inventory.
/// </summary>
public static class DiamondGltf
{
    private const int Sides = 8;

    public static byte[] Generate()
    {
        var positions = new List<(float X, float Y, float Z)>();
        var normals = new List<(float X, float Y, float Z)>();

        var table = Ring(Sides, 0.53f, 0.34f, 0f);
        var star = Ring(Sides, 0.80f, 0.20f, 22.5f);
        var girdleTop = Ring(Sides, 1.00f, 0.02f, 0f);
        var girdleBottom = Ring(Sides, 1.00f, -0.02f, 0f);
        var pavilionMid = Ring(Sides, 0.50f, -0.45f, 22.5f);
        var apex = (0f, -0.86f, 0f);
        var tableCenter = (0f, 0.34f, 0f);

        void AddTri((float, float, float) p0, (float, float, float) p1, (float, float, float) p2)
        {
            var n = Normalize(Cross(Sub(p1, p0), Sub(p2, p0)));
            var c = ((p0.Item1 + p1.Item1 + p2.Item1) / 3f, (p0.Item2 + p1.Item2 + p2.Item2) / 3f, (p0.Item3 + p1.Item3 + p2.Item3) / 3f);
            if (Dot(n, c) < 0)
            {
                (p1, p2) = (p2, p1);
                n = Normalize(Cross(Sub(p1, p0), Sub(p2, p0)));
            }

            positions.Add(p0); positions.Add(p1); positions.Add(p2);
            normals.Add(n); normals.Add(n); normals.Add(n);
        }

        void ZigzagBand(IReadOnlyList<(float, float, float)> a, IReadOnlyList<(float, float, float)> b)
        {
            for (var i = 0; i < a.Count; i++)
            {
                var a0 = a[i]; var a1 = a[(i + 1) % a.Count];
                var b0 = b[i]; var b1 = b[(i + 1) % b.Count];
                AddTri(a0, a1, b0);
                AddTri(a1, b1, b0);
            }
        }

        void StraightBand(IReadOnlyList<(float, float, float)> a, IReadOnlyList<(float, float, float)> b)
        {
            for (var i = 0; i < a.Count; i++)
            {
                var a0 = a[i]; var a1 = a[(i + 1) % a.Count];
                var b0 = b[i]; var b1 = b[(i + 1) % b.Count];
                AddTri(a0, a1, b1);
                AddTri(a0, b1, b0);
            }
        }

        void Fan((float, float, float) center, IReadOnlyList<(float, float, float)> ring)
        {
            for (var i = 0; i < ring.Count; i++)
            {
                AddTri(center, ring[i], ring[(i + 1) % ring.Count]);
            }
        }

        Fan(tableCenter, table);
        ZigzagBand(table, star);
        ZigzagBand(star, girdleTop);
        StraightBand(girdleTop, girdleBottom);
        ZigzagBand(girdleBottom, pavilionMid);
        Fan(apex, pavilionMid);

        return Pack(positions, normals);
    }

    private static List<(float X, float Y, float Z)> Ring(int n, float radius, float y, float rotationDegrees)
    {
        var pts = new List<(float, float, float)>(n);
        for (var i = 0; i < n; i++)
        {
            var angle = double.DegreesToRadians(rotationDegrees + i * (360.0 / n));
            pts.Add(((float)(radius * Math.Cos(angle)), y, (float)(radius * Math.Sin(angle))));
        }

        return pts;
    }

    private static (float, float, float) Sub((float X, float Y, float Z) a, (float X, float Y, float Z) b)
        => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static (float, float, float) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b)
        => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static float Dot((float X, float Y, float Z) a, (float X, float Y, float Z) b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static (float, float, float) Normalize((float X, float Y, float Z) a)
    {
        var len = MathF.Sqrt(Dot(a, a));
        return len < 1e-8f ? a : (a.X / len, a.Y / len, a.Z / len);
    }

    private static byte[] Pack(List<(float X, float Y, float Z)> positions, List<(float X, float Y, float Z)> normals)
    {
        var vertexCount = positions.Count;

        using var posBytes = new MemoryStream();
        using var posWriter = new BinaryWriter(posBytes);
        foreach (var p in positions)
        {
            posWriter.Write(p.X); posWriter.Write(p.Y); posWriter.Write(p.Z);
        }

        using var nrmBytes = new MemoryStream();
        using var nrmWriter = new BinaryWriter(nrmBytes);
        foreach (var n in normals)
        {
            nrmWriter.Write(n.X); nrmWriter.Write(n.Y); nrmWriter.Write(n.Z);
        }

        using var uvBytes = new MemoryStream();
        using var uvWriter = new BinaryWriter(uvBytes);
        (float U, float V)[] triUv = [(0.5f, 1f), (0f, 0f), (1f, 0f)];
        for (var i = 0; i < vertexCount; i++)
        {
            var texCoord = triUv[i % 3];
            uvWriter.Write(texCoord.U); uvWriter.Write(texCoord.V);
        }

        using var idxBytes = new MemoryStream();
        using var idxWriter = new BinaryWriter(idxBytes);
        for (var i = 0; i < vertexCount; i++)
        {
            idxWriter.Write((ushort)i);
        }

        var pos = Pad4(posBytes.ToArray());
        var nrm = Pad4(nrmBytes.ToArray());
        var uv = Pad4(uvBytes.ToArray());
        var idx = Pad4(idxBytes.ToArray());

        var buffer = new byte[pos.Length + nrm.Length + uv.Length + idx.Length];
        Buffer.BlockCopy(pos, 0, buffer, 0, pos.Length);
        Buffer.BlockCopy(nrm, 0, buffer, pos.Length, nrm.Length);
        Buffer.BlockCopy(uv, 0, buffer, pos.Length + nrm.Length, uv.Length);
        Buffer.BlockCopy(idx, 0, buffer, pos.Length + nrm.Length + uv.Length, idx.Length);

        var minX = positions.Min(p => p.X); var maxX = positions.Max(p => p.X);
        var minY = positions.Min(p => p.Y); var maxY = positions.Max(p => p.Y);
        var minZ = positions.Min(p => p.Z); var maxZ = positions.Max(p => p.Z);

        var gltf = new
        {
            asset = new { version = "2.0", generator = "diamant-brilliant-cut-generator" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0, name = "BrilliantCutDiamond" } },
            meshes = new[]
            {
                new
                {
                    name = "Diamond",
                    primitives = new[]
                    {
                        new
                        {
                            attributes = new { POSITION = 0, NORMAL = 1, TEXCOORD_0 = 2 },
                            indices = 3,
                            material = 0
                        }
                    }
                }
            },
            materials = new[]
            {
                new
                {
                    name = "Diamond",
                    pbrMetallicRoughness = new
                    {
                        baseColorFactor = new[] { 0.92, 0.97, 1.0, 1.0 },
                        metallicFactor = 0.0,
                        roughnessFactor = 0.02
                    },
                    extensions = new Dictionary<string, object>
                    {
                        ["KHR_materials_transmission"] = new { transmissionFactor = 1.0 },
                        ["KHR_materials_ior"] = new { ior = 2.42 },
                        ["KHR_materials_volume"] = new { thicknessFactor = 1.0, attenuationDistance = 3.0, attenuationColor = new[] { 0.9, 0.97, 1.0 } },
                        ["KHR_materials_specular"] = new { specularFactor = 1.0, specularColorFactor = new[] { 1.0, 1.0, 1.0 } },
                    },
                    alphaMode = "OPAQUE",
                    doubleSided = false
                }
            },
            extensionsUsed = new[] { "KHR_materials_transmission", "KHR_materials_ior", "KHR_materials_volume", "KHR_materials_specular" },
            buffers = new[]
            {
                new
                {
                    uri = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer),
                    byteLength = buffer.Length
                }
            },
            bufferViews = new[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = pos.Length, target = 34962 },
                new { buffer = 0, byteOffset = pos.Length, byteLength = nrm.Length, target = 34962 },
                new { buffer = 0, byteOffset = pos.Length + nrm.Length, byteLength = uv.Length, target = 34962 },
                new { buffer = 0, byteOffset = pos.Length + nrm.Length + uv.Length, byteLength = idx.Length, target = 34963 },
            },
            accessors = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = vertexCount, type = "VEC3", min = new[] { minX, minY, minZ }, max = new[] { maxX, maxY, maxZ } },
                new { bufferView = 1, componentType = 5126, count = vertexCount, type = "VEC3" },
                new { bufferView = 2, componentType = 5126, count = vertexCount, type = "VEC2" },
                new { bufferView = 3, componentType = 5123, count = vertexCount, type = "SCALAR" },
            }
        };

        return JsonSerializer.SerializeToUtf8Bytes(gltf);
    }

    private static byte[] Pad4(byte[] bytes)
    {
        var pad = (4 - (bytes.Length % 4)) % 4;
        if (pad == 0)
        {
            return bytes;
        }

        var padded = new byte[bytes.Length + pad];
        Buffer.BlockCopy(bytes, 0, padded, 0, bytes.Length);
        return padded;
    }
}
