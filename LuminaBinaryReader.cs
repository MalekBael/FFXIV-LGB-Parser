using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace LgbParser
{
    public class LuminaBinaryReader : BinaryReader
    {
        public long Position
        {
            get => BaseStream.Position;
            set => BaseStream.Position = value;
        }

        public LuminaBinaryReader(byte[] array) : base(new MemoryStream(array, false), Encoding.UTF8, false)
        {
        }

        public int[] ReadInt32Array(int count)
        {
            var array = new int[count];
            for (int i = 0; i < count; i++)
            {
                array[i] = ReadInt32();
            }
            return array;
        }

        public char[] ReadChars(int count)
        {
            return base.ReadChars(count);
        }
    }
}