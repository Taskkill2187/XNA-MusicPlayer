﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Windows.Forms;
using System.Threading;
using NAudio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using System.Runtime.InteropServices;
using MusicPlayerwNAudio;
using System.Threading.Tasks;

namespace MusicPlayer
{
    public static class Assets
    {
        public static SpriteFont Font;
        public static SpriteFont Title;

        public static Texture2D White;
        public static Texture2D bg;
        public static Texture2D bg1;
        public static Texture2D bg2;
        public static Texture2D Volume;

        public static Effect gaussianBlurHorz;
        public static Effect gaussianBlurVert;
        public static Effect PixelBlur;
        
        // Music Player Manager Values
        public static string currentlyPlayingSongName
        {
            get
            {
                return PlayerHistory[PlayerHistoryIndex].Split('\\').Last();
            }
        }
        public static string currentlyPlayingSongPath
        {
            get
            {
                return PlayerHistory[PlayerHistoryIndex];
            }
        }
        public static List<string> Playlist = new List<string>();
        public static List<string> PlayerHistory = new List<string>();
        public static int PlayerHistoryIndex = 0;
        public static int SongChangedTickTime = -10000;
        public static int SongStartTime;

        // MultiThreading
        public static Task T = null;
        public static bool AbortAbort = false;

        // NAudio
        public static WaveChannel32 Channel32;
        public static WaveChannel32 Channel32Reader;
        public static DirectSoundOut output;
        public static Mp3FileReader mp3;
        public static Mp3FileReader mp3Reader;
        public static MMDevice device;
        public static MMDeviceEnumerator enumerator;
        //public const int bufferLength = 8192;
        //public const int bufferLength = 16384;
        //public const int bufferLength = 32768;
        public const int bufferLength = 65536;
        //public const int bufferLength = 131072; 
        //public const int bufferLength = 262144;
        public static List<float> EntireSongWaveBuffer;
        public static byte[] buffer;
        public static float[] WaveBuffer;
        public static float[] FFToutput;
        public static float[] RawFFToutput;
        
        // Data Management
        public static void Load(ContentManager Content, GraphicsDevice GD)
        {
            Console.WriteLine("Loading Effects...");
            gaussianBlurHorz = Content.Load<Effect>("GaussianBlurHorz");
            gaussianBlurVert = Content.Load<Effect>("GaussianBlurVert");
            PixelBlur = Content.Load<Effect>("PixelBlur");


            Console.WriteLine("Loading Textures...");
            White = new Texture2D(GD, 1, 1);
            Color[] Col = new Color[1];
            Col[0] = Color.White;
            White.SetData(Col);


            Volume = Content.Load<Texture2D>("volume");
            bg1 = Content.Load<Texture2D>("bg1");
            bg2 = Content.Load<Texture2D>("bg2");


            Console.WriteLine("Loading Fonts...");
            Font = Content.Load<SpriteFont>("Font");
            Title = Content.Load<SpriteFont>("Title");


            Console.WriteLine("Loading Background...");
            RegistryKey UserWallpaper = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop", false);
            if (Convert.ToInt32(UserWallpaper.GetValue("WallpaperStyle")) != 2)
            {
                MessageBox.Show("The background won't work if the Desktop WallpaperStyle isn't set to stretch! \nDer Hintergrund wird nicht funktionieren, wenn der Dektop WallpaperStyle nicht auf Dehnen gesetzt wurde!");
            }
            FileStream Stream = new FileStream(UserWallpaper.GetValue("WallPaper").ToString(), FileMode.Open);
            bg = Texture2D.FromStream(GD, Stream);
            Stream.Dispose();
            SystemEvents.UserPreferenceChanged += new UserPreferenceChangedEventHandler((object o, UserPreferenceChangedEventArgs target) => { RefreshBGtex(GD); });

            
            Console.WriteLine("Loading Songs...");
            if (Directory.Exists(config.Default.MusicPath))
                FindAllMp3FilesInDir(config.Default.MusicPath);
            else
            {
                FolderBrowserDialog open = new FolderBrowserDialog();
                open.Description = "Select your music folder";
                if (open.ShowDialog() != DialogResult.OK) Process.GetCurrentProcess().Kill();
                config.Default.MusicPath = open.SelectedPath;
                FindAllMp3FilesInDir(open.SelectedPath);
            }
            Console.WriteLine("Found " + Playlist.Count.ToString() + " Songs!");


            Console.WriteLine("Monitoring System Audio output...");
            enumerator = new MMDeviceEnumerator();
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);


            Console.WriteLine("Starting first Song...");
            if (Playlist.Count > 0)
            {
                if (Program.args.Length > 0)
                    PlayNewSong(Program.args[0]);
                else
                {
                    int PlaylistIndex = Values.RDM.Next(Playlist.Count);
                    GetNextSong(true);
                    PlayerHistory.Add(Playlist[PlaylistIndex]);
                }
            }
            else
                Console.WriteLine("Playlist empty!");

            Console.WriteLine("Loading GUI...");
            Values.MinimizeConsole();
        }
        public static void FindAllMp3FilesInDir(string StartDir)
        {
            foreach (string s in Directory.GetFiles(StartDir))
                if (s.EndsWith(".mp3"))
                    Playlist.Add(s);

            foreach (string D in Directory.GetDirectories(StartDir))
                FindAllMp3FilesInDir(D);
        }
        public static void RefreshBGtex(GraphicsDevice GD)
        {
            lock (bg)
            {
                RegistryKey UserWallpaper = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop", false);
                FileStream Stream = new FileStream(UserWallpaper.GetValue("WallPaper").ToString(), FileMode.Open);
                bg = Texture2D.FromStream(GD, Stream);
                Stream.Dispose();
            }
        }
        public static void DisposeNAudioData()
        {
            if (output != null)
            {
                if (output.PlaybackState == PlaybackState.Playing) output.Stop();
                output.Dispose();
                output = null;
            }
            if (Channel32 != null)
            {
                try
                {
                    Channel32.Dispose();
                    Channel32 = null;
                }
                catch { }
            }
            if (Channel32Reader != null)
            {
                try
                {
                    Channel32Reader.Dispose();
                } catch { Debug.WriteLine("Couldn't dispose the reader"); }
                Channel32Reader = null;
            }
            if (mp3 != null)
            {
                mp3.Dispose();
                mp3 = null;
            }
        }

        // Visualization
        public static void UpdateWaveBuffer()
        {
            buffer = new byte[bufferLength];
            WaveBuffer = new float[bufferLength / 4];

            if (Channel32 != null && Channel32Reader != null && Channel32Reader.CanRead)
            {
                Channel32Reader.Position = Channel32.Position;

                while (true)
                {
                    try
                    {
                        int Read = Channel32Reader.Read(buffer, 0, bufferLength);
                        break;
                    }
                    catch { Debug.WriteLine("AHAHHAHAHAHA.... ich kann nicht lesen"); }
                }

                // Converting the byte buffer in readable data
                for (int i = 0; i < bufferLength / 4; i++)
                    WaveBuffer[i] = BitConverter.ToSingle(buffer, i * 4);
            }
        }
        public static void UpdateFFTbuffer()
        {
            Complex[] tempbuffer = new Complex[WaveBuffer.Length];

            lock (WaveBuffer)
            {
                for (int i = 0; i < tempbuffer.Length; i++)
                {
                    tempbuffer[i].X = (float)(WaveBuffer[i] * FastFourierTransform.HammingWindow(i, tempbuffer.Length));
                    tempbuffer[i].Y = 0;
                }
            }

            FastFourierTransform.FFT(true, (int)Math.Log(tempbuffer.Length, 2.0), tempbuffer);
            
            FFToutput = new float[tempbuffer.Length / 2 - 1];
            RawFFToutput = new float[tempbuffer.Length / 2 - 1];
            for (int i = 0; i < FFToutput.Length; i++)
            {
                RawFFToutput[i] = (float)(Math.Log10(1 + Math.Sqrt((tempbuffer[i].X * tempbuffer[i].X) + (tempbuffer[i].Y * tempbuffer[i].Y))) * 10);
                FFToutput[i] = (float)(RawFFToutput[i] * Math.Sqrt(i + 1));
            }
        }
        public static void UpdateEntireSongBuffers()
        {
            try {
                lock (Channel32Reader)
                {
                    byte[] buffer = new byte[16384];
                    Channel32Reader.Position = 0;
                    EntireSongWaveBuffer = new List<float>();

                    while (Channel32Reader.Position < Channel32Reader.Length)
                    {
                        if (AbortAbort)
                            break;

                        int read = Channel32Reader.Read(buffer, 0, 16384);

                        if (AbortAbort)
                            break;

                        for (int i = 0; i < read / 4; i++)
                        {
                            if (EntireSongWaveBuffer.Count < 67108864)
                                EntireSongWaveBuffer.Add(BitConverter.ToSingle(buffer, i * 4));

                            if (AbortAbort)
                                break;
                        }
                    }

                    AbortAbort = false;
                }
            } catch {
                Debug.WriteLine("Couldn't load " + currentlyPlayingSongPath);
                Debug.WriteLine("SongBuffer Length: " + EntireSongWaveBuffer.Count);
                DisposeNAudioData();
                PlayerHistory.RemoveAt(PlayerHistory.Count - 1);
                PlayerHistoryIndex = PlayerHistory.Count - 1;
                GetNextSong(true);
            }
        }
        public static void UpdateWaveBufferWithEntireSongWB()
        {
            lock (EntireSongWaveBuffer)
            {
                WaveBuffer = new float[bufferLength / 4];
                if (Channel32 != null && EntireSongWaveBuffer.Count > bufferLength && Channel32.Position > bufferLength && Channel32.Position / 4 < 67108864)
                    WaveBuffer = EntireSongWaveBuffer.GetRange((int)(Channel32.Position / 4 - bufferLength / 4), bufferLength / 4).ToArray();
                else
                    for (int i = 0; i < bufferLength / 4; i++)
                        WaveBuffer[i] = 0;
            }
        }
        public static float GetAverageHeight(float[] array, int from, int to)
        {
            float temp = 0;

            if (from < 0)
                from = 0;

            if (to > array.Length)
                to = array.Length;

            for (int i = from; i < to; i++)
                temp += array[i];
            
            return temp / array.Length;
        }
        public static float GetMaxHeight(float[] array, int from, int to)
        {
            if (from < 0)
                from = 0;

            if (to > array.Length)
                to = array.Length;

            if (from >= to)
                to = from + 1;

            float max = 0;
            for (int i = from; i < to; i++)
                if (array[i] > max)
                    max = array[i];

            return max;
        }

        // Music Player Managment
        public static void PlayPause()
        {
            if (output != null)
            {
                if (output.PlaybackState == PlaybackState.Playing) output.Pause();
                else if (output.PlaybackState == PlaybackState.Paused || output.PlaybackState == PlaybackState.Stopped) output.Play();
            }
        }
        public static void GetNewPlaylistSong()
        {
            int PlaylistIndex = Values.RDM.Next(Playlist.Count);
            PlayerHistory.Add(Playlist[PlaylistIndex]);
            PlayerHistoryIndex = PlayerHistory.Count - 1;
            PlaySongByPath(PlayerHistory[PlayerHistoryIndex]);
        }
        public static void PlayNewSong(string Path)
        {
            if (Values.Timer > SongChangedTickTime + 5 && !config.Default.MultiThreading ||
                config.Default.MultiThreading)
            {
                Path = Path.Trim('"');

                if (!File.Exists(Path))
                {
                    List<string> Hits = Playlist.FindAll(x => x.Contains(Path, StringComparison.InvariantCultureIgnoreCase));
                    
                    if (Hits.Count > 0)
                    {
                        Path = Hits[Values.RDM.Next(Hits.Count)];
                        Console.ForegroundColor = ConsoleColor.Green;
                        if (Hits.Count == 1)
                            Console.WriteLine(">Found one matching song: " + Path.Split('\\').Last());
                        else
                            Console.WriteLine(">Found " + Hits.Count + " matching songs and choose " + Path.Split('\\').Last());
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(">What the fuck is this supposed to mean!?");
                        return;
                    }
                }

                PlayerHistory.Add(Path);
                PlayerHistoryIndex = PlayerHistory.Count - 1;

                if (!Playlist.Contains(Path))
                    Playlist.Add(Path);

                PlaySongByPath(Path);

                SongChangedTickTime = Values.Timer;
            }
        }
        public static void GetNextSong(bool forced)
        {
            if (forced || Values.Timer > SongChangedTickTime + 5 && !config.Default.MultiThreading ||
                config.Default.MultiThreading)
            {
                PlayerHistoryIndex++;
                if (PlayerHistoryIndex > PlayerHistory.Count - 1)
                    GetNewPlaylistSong();
                else
                    PlaySongByPath(PlayerHistory[PlayerHistoryIndex]);

                SongChangedTickTime = Values.Timer;
            }
        }
        public static void GetPreviousSong()
        {
            if (Values.Timer > SongChangedTickTime + 30)
            {
                if (PlayerHistoryIndex > 0)
                {
                    PlayerHistoryIndex--;

                    PlaySongByPath(PlayerHistory[PlayerHistoryIndex]);
                }

                SongChangedTickTime = Values.Timer;
            }
        }
        private static void PlaySongByPath(string PathString)
        {
            if (T != null && T.Status == TaskStatus.Running)
            {
                AbortAbort = true;
                T.Wait();
            }

            DisposeNAudioData();

            if (PathString.Contains("\""))
                PathString = PathString.Trim(new char[] { '"', ' '});

            mp3 = new Mp3FileReader(PathString);
            mp3Reader = new Mp3FileReader(PathString);
            Channel32 = new WaveChannel32(mp3);
            Channel32Reader = new WaveChannel32(mp3Reader);

            var meter = new MeteringSampleProvider(mp3.ToSampleProvider());
            meter.StreamVolume += (s, e) => Debug.WriteLine("{0} - {1}", e.MaxSampleValues[0], e.MaxSampleValues[1]);

            output = new DirectSoundOut();
            output.Init(Channel32);

            if (config.Default.MultiThreading)
                T = Task.Factory.StartNew(UpdateEntireSongBuffers);
            else
                UpdateEntireSongBuffers();

            output.Play();
            Channel32.Volume = 0;
            SongStartTime = Values.Timer;
            Channel32.Position = 1;
        }

        // Draw Methods
        public static void DrawLine(Vector2 End1, Vector2 End2, int Thickness, Color Col, SpriteBatch SB)
        {
            Vector2 Line = End1 - End2;
            SB.Draw(White, End1, null, Col, -(float)Math.Atan2(Line.X, Line.Y) - (float)Math.PI / 2, new Vector2(0, 0.5f), new Vector2(Line.Length(), Thickness), SpriteEffects.None, 0f);
        }
        public static void DrawCircle(Vector2 Pos, float Radius, Color Col, SpriteBatch SB)
        {
            if (Radius < 0)
                Radius *= -1;

            for (int i = -(int)Radius; i < (int)Radius; i++)
            {
                int HalfHeight = (int)Math.Sqrt(Radius * Radius - i * i);
                SB.Draw(White, new Rectangle((int)Pos.X + i, (int)Pos.Y - HalfHeight, 1, HalfHeight * 2), Col);
            }
        }
        public static void DrawCircle(Vector2 Pos, float Radius, float HeightMultiplikator, Color Col, SpriteBatch SB)
        {
            if (Radius < 0)
                Radius *= -1;

            for (int i = -(int)Radius; i < (int)Radius; i++)
            {
                int HalfHeight = (int)Math.Sqrt(Radius * Radius - i * i);
                SB.Draw(White, new Rectangle((int)Pos.X + i, (int)Pos.Y, 1, (int)(HalfHeight * HeightMultiplikator)), Col);
            }

            for (int i = -(int)Radius; i < (int)Radius; i++)
            {
                int HalfHeight = (int)Math.Sqrt(Radius * Radius - i * i);
                SB.Draw(White, new Rectangle((int)Pos.X + i + 1, (int)Pos.Y, -1, (int)(-HalfHeight * HeightMultiplikator)), Col);
            }
        }
        public static void DrawRoundedRectangle(Rectangle Rect, float PercentageOfRounding, Color Col, SpriteBatch SB)
        {
            float Rounding = PercentageOfRounding / 100;
            Rectangle RHorz = new Rectangle(Rect.X, (int)(Rect.Y + Rect.Height * (Rounding / 2)), Rect.Width, (int)(Rect.Height * (1-Rounding)));
            Rectangle RVert = new Rectangle((int)(Rect.X + Rect.Width * (Rounding / 2)), Rect.Y, (int)(Rect.Width * (1-Rounding)), (int)(Rect.Height * 0.999f));

            int RadiusHorz = (int)(Rect.Width * (Rounding / 2));
            int RadiusVert = (int)(Rect.Height * (Rounding / 2));

            if (RadiusHorz != 0)
            {
                // Top-Left
                DrawCircle(new Vector2(Rect.X + RadiusHorz, Rect.Y + RadiusVert), RadiusHorz, RadiusVert / (float)RadiusHorz, Col, SB);

                // Top-Right
                DrawCircle(new Vector2(Rect.X + Rect.Width - RadiusHorz - 1, Rect.Y + RadiusVert), RadiusHorz, RadiusVert / (float)RadiusHorz, Col, SB);

                // Bottom-Left
                DrawCircle(new Vector2(Rect.X + RadiusHorz, Rect.Y + RadiusVert + (int)(Rect.Height * (1 - Rounding))), RadiusHorz, RadiusVert / (float)RadiusHorz, Col, SB);

                // Bottom-Right
                DrawCircle(new Vector2(Rect.X + Rect.Width - RadiusHorz -1, Rect.Y + RadiusVert + (int)(Rect.Height * (1 - Rounding))), RadiusHorz, RadiusVert / (float)RadiusHorz, Col, SB);
            }

            SB.Draw(White, RHorz, Col);
            SB.Draw(White, RVert, Col);
        }
    }
}