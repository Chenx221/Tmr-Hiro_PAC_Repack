
using System.IO;
using System.Text;

namespace TmrHiroRepack
{
    public class Entry
    {
        public string Name { get; set; }
        public long Offset { get; set; }
        public uint Size { get; set; }
    }
    public class Index
    {
        public string name;
        public EntryInfoV1 entryInfoV1;
        public EntryInfoV2 entryInfoV2;
    }
    public class EntryInfoV1
    {
        public uint entryOffset;
        public uint entrySize;
    }
    public class EntryInfoV2
    {
        public long entryOffset;
        public uint entrySize;
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: TmrHiroRepack <FolderPath> <Version>");
                return;
            }

            string folderPath = args[0];
            string versionArg = args[1];

            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine("Invalid FolderPath");
                return;
            }

            if (!int.TryParse(versionArg, out int version) || (version != 1 && version != 2))
            {
                Console.WriteLine("Invalid Version");
                return;
            }

            Console.WriteLine($"Repacking {Path.GetFileName(folderPath)}");
            bool status = Repack(folderPath, version);
            Console.WriteLine(status ? "Repack Successful" : "Repack Failed");
        }

        private static bool Repack(string folderPath, int version)
        {
            EncodingProvider provider = CodePagesEncodingProvider.Instance;
            Encoding? shiftJis = provider.GetEncoding("shift-jis");
            string[] files = Directory.GetFiles(folderPath);
            short count = (short)files.Length; //文件数量
            string[] extensions = [".ogg", ".grd", ".srp"]; //需要移除的文件后缀，因为这是Garbro添加的
            int max_name_length = 0;
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                foreach (var extension in extensions)
                {
                    if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = fileName[..^extension.Length];
                    }
                }
                max_name_length = Math.Max(max_name_length, shiftJis.GetBytes(fileName).Length);
            }
            byte name_length = (byte)max_name_length; //文件名长度,每个封包值都不同
            uint data_offset;
            if (version == 1)
                data_offset = 7 + ((uint)name_length + 8) * (uint)count; //Data区偏移
            else if (version == 2)
                data_offset = 7 + ((uint)name_length + 12) * (uint)count; //Data区偏移
            else
                throw new Exception("Invalid Version");
            long offset = 0; //Index区偏移
            List<Index> indexs = [];
            foreach (string file in files)
            {
                Index i = new();
                string fileName = Path.GetFileName(file);
                FileInfo fileInfo = new(file);
                //remove .ogg/.grd/.srp
                foreach (var extension in extensions)
                {
                    if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = fileName[..^extension.Length];
                    }
                }
                i.name = fileName;
                if (version == 1)
                {
                    i.entryInfoV1 = new()
                    {
                        entryOffset = (uint)offset,
                        entrySize = (uint)fileInfo.Length
                    };
                }

                else if (version == 2)
                {
                    i.entryInfoV2 = new()
                    {
                        entryOffset = offset,
                        entrySize = (uint)fileInfo.Length
                    };
                }
                offset += fileInfo.Length;
                indexs.Add(i);
            }
            //准备开写
            string outputPath = Path.Combine(Path.GetDirectoryName(folderPath), Path.GetFileName(folderPath) + ".pac");
            using FileStream fs = new(outputPath, FileMode.Create);
            using BinaryWriter bw = new(fs);
            bw.Write(count);//文件数量
            bw.Write(name_length);//文件名长度
            bw.Write(data_offset);//Data区偏移
            foreach (Index i in indexs)
            {
                byte[] name = new byte[name_length];
                Array.Copy(shiftJis.GetBytes(i.name), name, shiftJis.GetBytes(i.name).Length);
                bw.Write(name);//文件名
                if (version == 1)
                {
                    bw.Write(i.entryInfoV1.entryOffset);//文件偏移
                    bw.Write(i.entryInfoV1.entrySize);//文件大小
                }
                else if (version == 2)
                {
                    bw.Write(i.entryInfoV2.entryOffset);//文件偏移
                    bw.Write(i.entryInfoV2.entrySize);//文件大小
                }
            }
            foreach (string file in files)
            {
                //检查是否是脚本文件(文件后缀.srp)
                if (Path.GetExtension(file) == ".srp") // Script file detected
                {
                    byte[] DecryptData = File.ReadAllBytes(file);
                    uint offset2 = 0;
                    int record_count = BitConverter.ToInt32(DecryptData, (int)offset2);
                    offset2 += 4;
                    for (int i = 0; i < record_count; i++)
                    {
                        ushort chunk_size = (ushort)(BitConverter.ToUInt16(DecryptData, (int)offset2) - 4); //我知道这里有潜在的问题，但我不相信哪个游戏单个script文件有2G
                        offset2 += 6;
                        for (int j = 0; j < chunk_size; j++)
                        {
                            DecryptData[offset2] = Utils.RotByteL(DecryptData[offset2], 4);
                            offset2++;
                        }
                    }
                    bw.Write(DecryptData);
                }
                else
                {
                    using FileStream fs2 = new(file, FileMode.Open);
                    fs2.CopyTo(fs);
                }

            }
            return true;
        }
    }
}
