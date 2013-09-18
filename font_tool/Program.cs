using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Linq;

namespace font_tool
{
    class Glyph
    {
        public Bitmap image;
        public Rectangle source_rect;
        public Point offset;
        public List<Character> characters = new List<Character>();
    }

    class Character
    {
        public int id;
        public float x_offset, y_offset;
        public float x_advance;
        public int channel;
        public int page;
        public Glyph glyph;

        public Character()
        {

        }

        public Character(Character other)
        {
            id = other.id;
            x_offset = other.x_offset;
            y_offset = other.y_offset;
            x_advance = other.x_advance;
            channel = other.channel;
            page = other.page;
            glyph = other.glyph;
        }
    }

    class Font
    {
        public string filename;
        public string output_filename;
        public List<string> header_lines = new List<string>();
        public int line_height;
        public int base_offset;
        public SortedDictionary<int, Character> characters = new SortedDictionary<int, Character>();
        public SortedDictionary<int, SortedDictionary<int, float>> kernings = new SortedDictionary<int, SortedDictionary<int, float>>();
    }

    class Program
    {
        static bool dump_glyphs = false;
        static bool load_loose_glyphs = false;
        static Bitmap font_image = null;
        static List<Font> fonts = new List<Font>();
        static Dictionary<string, Dictionary<Rectangle, Glyph>> glyph_lookup = new Dictionary<string, Dictionary<Rectangle, Glyph>>();

        static void Reset()
        {
            if (font_image != null) font_image.Dispose();
            foreach (Dictionary<Rectangle, Glyph> glyph_rect_lookup in glyph_lookup.Values)
            {
                foreach (Glyph glyph in glyph_rect_lookup.Values)
                {
                    if (glyph.image != null) glyph.image.Dispose();
                }
            }

            font_image = null;
            fonts = new List<Font>();
            glyph_lookup = new Dictionary<string, Dictionary<Rectangle, Glyph>>();
        }

        static bool GetGlyph(string font_image_filename, Rectangle rect, out Glyph glyph)
        {
            Dictionary<Rectangle, Glyph> glyph_rect_lookup;
            if (!glyph_lookup.TryGetValue(font_image_filename, out glyph_rect_lookup))
            {
                glyph_rect_lookup = new Dictionary<Rectangle, Glyph>();
                glyph_lookup.Add(font_image_filename, glyph_rect_lookup);
            }

            if (!glyph_rect_lookup.TryGetValue(rect, out glyph))
            {
                glyph = new Glyph { source_rect = rect};
                glyph_rect_lookup.Add(rect, glyph);
                return false;
            }
            return true;
        }

        static void ParseField(string input, string name, out int value)
        {
            string[] data = input.Split('=');
            Debug.Assert(data[0] == name);
            value = int.Parse(data[1]);
        }

        static void ParseField(string input, string name, out float value)
        {
            string[] data = input.Split('=');
            Debug.Assert(data[0] == name);
            value = float.Parse(data[1]);
        }

        enum Mode
        {
            ReadHeader,
            ReadCharacters,
            ReadKernings,
        }

        static Font CloneFont(Font source)
        {
            Font font = new Font
            {
                filename = source.filename,
                line_height = source.line_height,
                base_offset = source.base_offset,
                header_lines = new List<string>(source.header_lines),
                characters = new SortedDictionary<int, Character>()
            };

            foreach (KeyValuePair<int, Character> source_character in source.characters)
            {
                font.characters[source_character.Key] = new Character(source_character.Value);
            }

            font.kernings = new SortedDictionary<int, SortedDictionary<int, float>>();
            foreach (KeyValuePair<int, SortedDictionary<int, float>> source_kerning in source.kernings)
            {
                font.kernings[source_kerning.Key] = new SortedDictionary<int, float>(source_kerning.Value);
            }

            fonts.Add(font);
            return font;
        }

        static Font ReadFont(string filename)
        {
            string[] lines = File.ReadAllLines(filename);

            string source_font_image_filename = null;
            Bitmap source_font_image = null;

            Font font = new Font
                        {
                            filename = filename
                        };

            Mode mode = Mode.ReadHeader;
            foreach (string line in lines)
            {
                string[] line_data;
                switch (mode)
                {
                    case Mode.ReadHeader:
                        if (line.StartsWith("chars"))
                        {
                            mode = Mode.ReadCharacters;
                            continue;
                        }
                        if (line.StartsWith("common"))
                        {
                            line_data = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            Debug.Assert(line_data[0] == "common");

                            // common lineHeight=60 base=46 scaleW=512 scaleH=512 pages=1
                            // 0      1             2       3          4          5

                            ParseField(line_data[1], "lineHeight", out font.line_height);
                            ParseField(line_data[2], "base", out font.base_offset);
                        }
                        else if (line.StartsWith("page"))
                        {
                            int file_start = line.IndexOf("file=");
                            source_font_image_filename = line.Substring(file_start + 5);
                            source_font_image_filename = source_font_image_filename.Replace("\"", "");
                            source_font_image_filename = Path.Combine(Path.GetDirectoryName(filename), source_font_image_filename);
                            source_font_image = new Bitmap(source_font_image_filename);
                            source_font_image.SetResolution(96, 96);
                        }
                        else
                        {
                            font.header_lines.Add(line);
                        }

                        break;
                    case Mode.ReadCharacters:
                        if (line.StartsWith("kernings"))
                        {
                            mode = Mode.ReadKernings;
                            continue;
                        }

                        Character character = new Character();
                        int id, x, y, w, h;

                        line_data = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        Debug.Assert(line_data[0] == "char");

                        //char id=32   x=248   y=38    width=5     height=5     xoffset=-2    yoffset=34    xadvance=8     page=0  chnl=15
                        //0    1       2       3       4           5            6             7             8              9       10
                        ParseField(line_data[1], "id", out id);
                        ParseField(line_data[2], "x", out x);
                        ParseField(line_data[3], "y", out y);
                        ParseField(line_data[4], "width", out w);
                        ParseField(line_data[5], "height", out h);
                        ParseField(line_data[6], "xoffset", out character.x_offset);
                        ParseField(line_data[7], "yoffset", out character.y_offset);
                        ParseField(line_data[8], "xadvance", out character.x_advance);
                        ParseField(line_data[9], "page", out character.page);
                        ParseField(line_data[10], "chnl", out character.channel);

                        Rectangle source_rect = new Rectangle(x, y, w, h);

                        Glyph glyph;
                        if (!GetGlyph(source_font_image_filename, source_rect, out glyph))
                        {
                            string basedir = "glyphs\\" + Path.GetFileNameWithoutExtension(font.filename);

                            Bitmap glyph_image;
                            if (load_loose_glyphs)
                            {
                                glyph_image = new Bitmap(basedir + "\\glyph_" + id + ".png");
                                glyph_image.SetResolution(96, 96);
                            }
                            else
                            {
                                glyph_image = new Bitmap(glyph.source_rect.Width, glyph.source_rect.Height);
                                BitmapTools.CopyRect(glyph_image, source_font_image, glyph.source_rect);
                                if (dump_glyphs)
                                {
                                    Directory.CreateDirectory(basedir);
                                    glyph_image.Save(basedir + "\\glyph_" + id + ".png");
                                }
                            }

                            glyph.image = Trim.TrimBitmapAlpha(glyph_image, 0, out glyph.offset);
                            glyph_image.Dispose();
                        }

                        character.id = id;
                        character.glyph = glyph;

                        glyph.characters.Add(character);
                        font.characters.Add(id, character);

                        break;
                    case Mode.ReadKernings:

                        int first;
                        int second;
                        float amount;

                        //kerning first=32  second=74  amount=-1  
                        //0       1         2          3

                        line_data = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        Debug.Assert(line_data[0] == "kerning");

                        ParseField(line_data[1], "first", out first);
                        ParseField(line_data[2], "second", out second);
                        ParseField(line_data[3], "amount", out amount);

                        if (!font.kernings.ContainsKey(first))
                        {
                            font.kernings[first] = new SortedDictionary<int, float>();
                        }

                        font.kernings[first][second] = amount;
                        break;
                }
            }

            source_font_image.Dispose();

            fonts.Add(font);
            return font;
        }

        static void WriteFont(Font font, string filename)
        {
            StringBuilder new_lines = new StringBuilder();
            foreach (string line in font.header_lines)
            {
                new_lines.AppendLine(line);
            }

            new_lines.AppendFormat("common lineHeight={0} base={1} scaleW={2} scaleH={3} pages=1\r\n", font.line_height, font.base_offset, font_image.Width, font_image.Height);
            new_lines.AppendFormat("page id=0 file=\"FontAtlas.png\"\r\n");

            new_lines.AppendFormat("chars count={0}\r\n", font.characters.Count);
            foreach (int id in font.characters.Keys)
            {
                Character character = font.characters[id];
                Glyph glyph = character.glyph;
                new_lines.AppendFormat("char id={0} x={1} y={2} width={3} height={4} ", id, glyph.source_rect.X, glyph.source_rect.Y, glyph.source_rect.Width, glyph.source_rect.Height);
                new_lines.AppendFormat("xoffset={0} yoffset={1} xadvance={2} ", character.x_offset + glyph.offset.X, character.y_offset + glyph.offset.Y, character.x_advance);
                new_lines.AppendFormat("page={0} chnl={1}\r\n", character.page, character.channel);
            }

            int count = font.kernings.Values.Sum(kerning => kerning.Count);

            new_lines.AppendFormat("kernings count={0}\r\n", count);

            foreach (int first in font.kernings.Keys)
            {
                SortedDictionary<int, float> kerning = font.kernings[first];
                foreach (int second in kerning.Keys)
                {
                    new_lines.AppendFormat("kerning first={0} second={1} amount={2}\r\n", first, second, kerning[second]);
                }
            }

            File.WriteAllText(filename, new_lines.ToString());
        }

        static int CompareGlyphArea(Glyph a, Glyph b)
        {
            int area_a = a.image.Height;//Width;// * a.image.Height;
            int area_b = b.image.Height;//Width;// * b.image.Height;
            return area_b - area_a;
        }

        static void CreateDirectoryForFile(string filename)
        {
            string directory = Path.GetDirectoryName(filename);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        }


        static void ProcessFont(Hashtable settings)
        {
            Reset();

            ArrayList sources = settings["sources"] as ArrayList;
            foreach (object source_obj in sources)
            {
                string source_filename;
                Hashtable source;

                if (source_obj is string)
                {
                    source_filename = (string)source_obj;

                    source = new Hashtable();
                    source["filename"] = source_filename;
                }
                else
                {
                    Debug.Assert(source_obj is Hashtable, "sourceObj not string or Hashtable");
                    source = (Hashtable)source_obj;
                }


                Font font;

                if (source.ContainsKey("copyFrom"))
                {
                    source_filename = source["copyFrom"] as string;
                    Font source_font = fonts.Find(f => f.filename == source_filename);
                    font = CloneFont(source_font);
                }
                else
                {
                    source_filename = source["filename"] as string;
                    font = ReadFont(source_filename);
                }

                if (!source.ContainsKey("output"))
                {
                    var filename_noext = Path.GetFileNameWithoutExtension(source_filename);
                    source["output"] = Path.Combine(filename_noext, Path.ChangeExtension(filename_noext, ".txt"));
                }

                font.output_filename = source["output"] as string;

                Hashtable extras = source["extra"] as Hashtable;
                if (extras != null)
                {
                    foreach (string character_code in extras.Keys)
                    {
                        Hashtable extra = extras[character_code] as Hashtable;

                        string glyph_image_path = Path.GetFullPath(extra["glyph"] as string);

                        Glyph glyph;
                        if (!GetGlyph(glyph_image_path, Rectangle.Empty, out glyph))
                        {
                            Bitmap glyph_image = new Bitmap(glyph_image_path);
                            glyph_image.SetResolution(96, 96);
                            glyph.image = Trim.TrimBitmapAlpha(glyph_image, 0, out glyph.offset);
                            glyph_image.Dispose();
                        }

                        int x_advance = glyph.image.Width;
                        if (extra.ContainsKey("blank") && (bool)extra["blank"] == true)
                        {
                            glyph = new Glyph
                            {
                                image = new Bitmap(1, 1)
                            };
                        }

                        Character character = new Character
                        {
                            glyph = glyph,
                            page = 0,
                            channel = 15,
                            x_offset = 0,
                            y_offset = -(font.line_height - font.base_offset),
                            x_advance = x_advance
                        };

                        if (extra.ContainsKey("x_advance"))
                        {
                            character.x_advance += (int)(double)extra["x_advance"];
                        }

                        if (extra.ContainsKey("y_offset"))
                        {
                            character.y_offset += (int)(double)extra["y_offset"];
                        }

                        font.characters.Remove(character_code[0]);
                        font.characters.Add(character_code[0], character);
                    }
                }

                if (source.ContainsKey("characters"))
                {
                    string characters = source["characters"] as string;
                    HashSet<char> character_list = new HashSet<char>(characters);

                    List<int> characters_to_remove = new List<int>();
                    foreach (int id in font.characters.Keys)
                    {
                        if (!character_list.Contains((char)id))
                        {
                            characters_to_remove.Add(id);
                        }
                    }

                    foreach (int id in characters_to_remove)
                    {
                        font.characters.Remove(id);
                    }
                }

                if (source.ContainsKey("extraYOffset"))
                {
                    int extra_offset = (int) (double) source["extraYOffset"];
                    foreach (Character character in font.characters.Values)
                    {
                        character.y_offset += extra_offset;
                    }
                }

                if (source.ContainsKey("extraXOffset"))
                {
                    int extra_offset = (int)(double)source["extraXOffset"];
                    foreach (Character character in font.characters.Values)
                    {
                        character.x_offset += extra_offset;
                    }
                }

                if (source.ContainsKey("extraXAdvance"))
                {
                    int extra_advance = (int)(double)source["extraXAdvance"];
                    foreach (Character character in font.characters.Values)
                    {
                        character.x_advance += extra_advance;
                    }
                }

                if (source.ContainsKey("tracking"))
                {
                    int tracking = (int)Math.Round((double)source["tracking"] / 1000.0 * font.line_height);
                    foreach (Character character in font.characters.Values)
                    {
                        character.x_advance += tracking;
                    }
                }

                bool has_upper_glyphs = true;
                for (int c = 65; c <= 90; ++c)
                {
                    if (!font.characters.ContainsKey(c))
                    {
                        has_upper_glyphs = false;
                        break;
                    };
                }

                if (has_upper_glyphs)
                {
                    bool has_lower_glyphs = true;
                    for (int c = 97; c <= 122; ++c)
                    {
                        if (font.characters.ContainsKey(c)) continue;

                        has_lower_glyphs = false;
                        break;
                    }

                    if (!has_lower_glyphs)
                    {
                        for (int c = 65; c <= 90; ++c)
                        {
                            int c2 = c + 32;
                            font.characters[c2] = font.characters[c];

                            if (font.kernings.ContainsKey(c))
                            {
                                font.kernings[c2] = new SortedDictionary<int, float>(font.kernings[c]);
                            }

                            foreach (int first in font.kernings.Keys)
                            {
                                SortedDictionary<int, float> kerning = font.kernings[first];
                                if (kerning.ContainsKey(c))
                                {
                                    kerning[c2] = kerning[c];
                                }
                            }
                        }
                    }
                }

                List<int> kernings_to_remove = new List<int>();
                foreach (int id in font.kernings.Keys)
                {
                    if (!font.characters.ContainsKey(id))
                    {
                        kernings_to_remove.Add(id);
                        continue;
                    }

                    SortedDictionary<int, float> second = font.kernings[id];
                    foreach (int second_id in second.Keys)
                    {
                        if (!font.characters.ContainsKey(second_id))
                        {
                            kernings_to_remove.Add(second_id);
                        }
                    }
                }

                foreach (int id in kernings_to_remove)
                {
                    font.kernings.Remove(id);

                    foreach (SortedDictionary<int, float> second in font.kernings.Values)
                    {
                        second.Remove(id);
                    }
                }
            }

            List<Glyph> glyphs = new List<Glyph>();
            foreach (Dictionary<Rectangle, Glyph> glyph_rect_lookup in glyph_lookup.Values)
            {
                glyphs.AddRange(glyph_rect_lookup.Values);
            }
            glyphs.Sort(CompareGlyphArea);

            Bitmap[] glyph_images = new Bitmap[glyphs.Count];
            for (int i = 0; i < glyphs.Count; ++i)
            {
                Glyph glyph = glyphs[i];
                glyph_images[i] = glyph.image;
                glyph.image.SetResolution(96, 96);
            }


            int padding = 1;
            Rectangle[] packed = PackTextures(out font_image, glyph_images, padding, 128, 128, 1024);

            string output_directory = Path.GetFullPath(settings["outputDirectory"] as string);
            string output_image = Path.Combine(output_directory, settings["outputImage"] as string);

            CreateDirectoryForFile(output_image);
            font_image.Save(output_image);

            for (int i = 0; i < glyphs.Count; ++i)
            {
                Glyph glyph = glyphs[i];
                glyph.source_rect = packed[i];
                glyph.source_rect.X -= padding;
                glyph.source_rect.Y -= padding;
                glyph.source_rect.Width  += padding * 2;
                glyph.source_rect.Height += padding * 2;

                glyph.offset.X += padding;
                glyph.offset.Y += padding;
            }

            foreach (Font font in fonts)
            {
                string filename = Path.Combine(output_directory, font.output_filename);
                CreateDirectoryForFile(filename);
                WriteFont(font, filename);
            }
        }

        static void Main(string[] args)
        {
            foreach (string s in args)
            {
                switch (s)
                {
                    case "-dump":
                        dump_glyphs = true;
                        break;
                    case "-loose":
                        load_loose_glyphs = true;
                        break;
                    default:
                        Directory.SetCurrentDirectory(Path.GetDirectoryName(Path.GetFullPath(s)));
                        string json = File.ReadAllText(s);
                        object json_object = Procurios.Public.JSON.JsonDecode(json);

                        Hashtable hashtable = json_object as Hashtable;
                        if (hashtable != null)
                        {
                            ProcessFont(hashtable);
                        }
                        break;
                }
            }

        }

        public static Rectangle[] PackTextures(out Bitmap texture, Bitmap[] textures, int padding, int width, int height, int maxSize)
        {
            if (width > maxSize && height > maxSize) { texture = null; return null; }
            if (width > maxSize || height > maxSize) { int temp = width; width = height; height = temp; }

            MaxRectsBinPack bp = new MaxRectsBinPack(width, height, false);
            Rectangle[] rects = new Rectangle[textures.Length];

            int total_padding = padding * 2;
            for (int i = 0; i < textures.Length; i++)
            {
                Image tex = textures[i];
                Rectangle rect = bp.Insert(tex.Width + total_padding, tex.Height + total_padding, MaxRectsBinPack.FreeRectChoiceHeuristic.RectBottomLeftRule);
                if (rect.Width == 0 || rect.Height == 0)
                {
                    return PackTextures(out texture, textures, padding, width * (width <= height ? 2 : 1), height * (height < width ? 2 : 1), maxSize);
                }

                rect.X += padding;
                rect.Y += padding;
                rect.Width -= total_padding;
                rect.Height -= total_padding;

                rects[i] = rect;
            }

            texture = new Bitmap(width, height);

            BitmapData dest_data = texture.LockBits(new Rectangle(Point.Empty, texture.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int i = 0; i < textures.Length; i++)
            {
                Rectangle rect = rects[i];
                BitmapTools.CopyRect(dest_data, rect.Location, textures[i]);
            }
            texture.UnlockBits(dest_data);

            return rects;
        }
    }

}
