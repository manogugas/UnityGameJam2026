using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarArena.EditorTools
{
    /// <summary>
    /// The web build has no audio filters, so the "heard from inside the car" muffling of the crowd can't be done
    /// live there. This bakes it: a copy of each crowd/ambience WAV run through the same kind of low-pass the
    /// desktop uses in the cabin (2-pole, 1100 Hz, Q 1), saved next to the original as "&lt;name&gt; (Cabin).wav".
    /// ArenaCrowd crossfades original and cabin copy on the web. Also sets the web import to decompress on load
    /// (streamed clips play through a plain HTML audio element there, with no pitch or reliable looping).
    /// </summary>
    public static class CabinAudioBaker
    {
        public const float CutoffHz = 1100f, Q = 1f;

        /// <summary>Bake (or re-bake) the cabin copy of a clip and return it.</summary>
        public static AudioClip Bake(AudioClip source)
        {
            if (source == null) return null;
            string srcPath = AssetDatabase.GetAssetPath(source);
            string dstPath = Path.Combine(Path.GetDirectoryName(srcPath), Path.GetFileNameWithoutExtension(srcPath) + " (Cabin).wav").Replace('\\', '/');
            ReadWav(srcPath, out short[] pcm, out int channels, out int rate);
            var outPcm = LowPass(pcm, channels, rate);
            WriteWav(dstPath, outPcm, channels, rate);
            AssetDatabase.ImportAsset(dstPath, ImportAssetOptions.ForceUpdate);
            WebImport(srcPath);
            WebImport(dstPath);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(dstPath);
        }

        /// <summary>Web: decompress on load (a real Web Audio buffer) instead of streaming.</summary>
        static void WebImport(string path)
        {
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            var s = imp.GetOverrideSampleSettings("WebGL");
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.AAC;
            s.quality = 0.6f;
            imp.SetOverrideSampleSettings("WebGL", s);
            imp.SaveAndReimport();
        }

        static short[] LowPass(short[] pcm, int channels, int rate)
        {
            // RBJ cookbook low-pass biquad.
            double w = 2.0 * Math.PI * CutoffHz / rate, cos = Math.Cos(w), alpha = Math.Sin(w) / (2.0 * Q);
            double a0 = 1.0 + alpha;
            double b0 = (1.0 - cos) * 0.5 / a0, b1 = (1.0 - cos) / a0, b2 = b0;
            double a1 = -2.0 * cos / a0, a2 = (1.0 - alpha) / a0;
            var output = new short[pcm.Length];
            for (int c = 0; c < channels; c++)
            {
                // Run twice round the loop so the filter's start-up state matches its end (seamless loop point).
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                for (int pass = 0; pass < 2; pass++)
                    for (int i = c; i < pcm.Length; i += channels)
                    {
                        double x = pcm[i];
                        double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                        x2 = x1; x1 = x; y2 = y1; y1 = y;
                        if (pass == 1) output[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(y)));
                    }
            }
            return output;
        }

        static void ReadWav(string path, out short[] pcm, out int channels, out int rate)
        {
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (new string(r.ReadChars(4)) != "RIFF") throw new Exception(path + ": not a RIFF file");
                r.ReadInt32();
                if (new string(r.ReadChars(4)) != "WAVE") throw new Exception(path + ": not a WAVE file");
                channels = 0; rate = 0; int bits = 0; pcm = null;
                while (r.BaseStream.Position < r.BaseStream.Length - 8)
                {
                    string id = new string(r.ReadChars(4));
                    int size = r.ReadInt32();
                    long next = r.BaseStream.Position + size + (size & 1);
                    if (id == "fmt ")
                    {
                        int format = r.ReadInt16();
                        channels = r.ReadInt16();
                        rate = r.ReadInt32();
                        r.ReadInt32(); r.ReadInt16();
                        bits = r.ReadInt16();
                        if (format != 1 || bits != 16) throw new Exception(path + ": only 16-bit PCM WAV is supported");
                    }
                    else if (id == "data")
                    {
                        pcm = new short[size / 2];
                        for (int i = 0; i < pcm.Length; i++) pcm[i] = r.ReadInt16();
                    }
                    r.BaseStream.Position = next;
                }
                if (pcm == null || channels == 0) throw new Exception(path + ": no audio data");
            }
        }

        static void WriteWav(string path, short[] pcm, int channels, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int dataBytes = pcm.Length * 2;
                w.Write("RIFF".ToCharArray()); w.Write(36 + dataBytes); w.Write("WAVE".ToCharArray());
                w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)channels);
                w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(dataBytes);
                foreach (var s in pcm) w.Write(s);
            }
        }
    }
}
