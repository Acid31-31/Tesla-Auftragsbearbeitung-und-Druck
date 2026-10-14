using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length < 2)
{
  Console.Error.WriteLine("Usage: MakeIco <input.png> <output.ico>");
  return 1;
}

var input = args[0];
var output = args[1];
var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };

using var source = Image.FromFile(input);
var pngBlobs = new List<byte[]>();
foreach (var size in sizes)
{
  using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
  using var g = Graphics.FromImage(bmp);
  g.Clear(Color.Transparent);
  g.InterpolationMode = InterpolationMode.HighQualityBicubic;
  g.SmoothingMode = SmoothingMode.HighQuality;
  g.PixelOffsetMode = PixelOffsetMode.HighQuality;
  g.CompositingQuality = CompositingQuality.HighQuality;
  g.DrawImage(source, new Rectangle(0, 0, size, size));
  using var ms = new MemoryStream();
  bmp.Save(ms, ImageFormat.Png);
  pngBlobs.Add(ms.ToArray());
}

await using var fs = File.Create(output);
await using var bw = new BinaryWriter(fs);
bw.Write((ushort)0);
bw.Write((ushort)1);
bw.Write((ushort)pngBlobs.Count);
var offset = 6 + 16 * pngBlobs.Count;
for (var i = 0; i < pngBlobs.Count; i++)
{
  var size = sizes[i];
  bw.Write((byte)(size >= 256 ? 0 : size));
  bw.Write((byte)(size >= 256 ? 0 : size));
  bw.Write((byte)0);
  bw.Write((byte)0);
  bw.Write((ushort)1);
  bw.Write((ushort)32);
  bw.Write(pngBlobs[i].Length);
  bw.Write(offset);
  offset += pngBlobs[i].Length;
}
foreach (var blob in pngBlobs)
  bw.Write(blob);

Console.WriteLine($"Wrote {output} ({new FileInfo(output).Length} bytes)");
return 0;
