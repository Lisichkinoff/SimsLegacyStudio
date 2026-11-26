using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SimsLegacy
{
    // Read only the protobuf fields needed by the importer. Slices share the original buffer.
    internal struct SaveFields
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal readonly byte[] Bytes;
        internal readonly int Start, End;

        internal SaveFields(byte[] bytes, int start, int end) { Bytes = bytes; Start = start; End = end; }
        internal SaveFields(byte[] bytes) : this(bytes, 0, bytes.Length) { }
        internal bool Present { get { return Bytes != null; } }

        internal struct Field
        {
            public int Number, Wire;
            public ulong NumberValue;
            public SaveFields Data;
        }

        internal IEnumerable<Field> Read()
        {
            if (!Present) yield break;
            int position = Start;
            while (position < End)
            {
                Field field;
                if (!TryRead(Bytes, ref position, End, out field))
                    throw new InvalidDataException("В сохранении есть повреждённая запись.");
                yield return field;
            }
        }

        internal static bool TryRead(byte[] bytes, ref int position, int end, out Field field)
        {
            field = new Field();
            ulong tag;
            if (!Varint(bytes, ref position, end, out tag) || tag < 8 || tag > UInt32.MaxValue) return false;
            field.Number = (int)(tag >> 3);
            field.Wire = (int)(tag & 7);
            if (field.Wire == 0) return Varint(bytes, ref position, end, out field.NumberValue);
            if (field.Wire == 2)
            {
                ulong length;
                if (!Varint(bytes, ref position, end, out length) || length > (ulong)(end - position)) return false;
                field.Data = new SaveFields(bytes, position, position + (int)length);
                position += (int)length;
                return true;
            }
            int size = field.Wire == 1 ? 8 : field.Wire == 5 ? 4 : 0;
            if (size == 0 || end - position < size) return false;
            field.NumberValue = size == 8 ? BitConverter.ToUInt64(bytes, position) : BitConverter.ToUInt32(bytes, position);
            position += size;
            return true;
        }

        private static bool Varint(byte[] bytes, ref int position, int end, out ulong value)
        {
            value = 0;
            for (int shift = 0; position < end && shift <= 63; shift += 7)
            {
                byte next = bytes[position++];
                if (shift == 63 && next > 1) return false;
                value |= (ulong)(next & 127) << shift;
                if (next < 128) return true;
            }
            return false;
        }

        internal ulong Number(int tag)
        {
            foreach (var field in Read())
                if (field.Number == tag && field.Wire != 2) return field.NumberValue;
            return 0;
        }

        internal IEnumerable<SaveFields> Messages(int tag)
        {
            foreach (var field in Read())
                if (field.Number == tag && field.Wire == 2) yield return field.Data;
        }

        internal IEnumerable<ulong> Ids(int tag, bool fixed64)
        {
            foreach (var field in Read())
            {
                if (field.Number != tag) continue;
                if (field.Wire != 2) { yield return field.NumberValue; continue; }
                int position = field.Data.Start, end = field.Data.End;
                while (position < end)
                {
                    ulong value;
                    if (fixed64)
                    {
                        if (end - position < 8) throw new InvalidDataException("Повреждён список симов.");
                        value = BitConverter.ToUInt64(Bytes, position);
                        position += 8;
                    }
                    else if (!Varint(Bytes, ref position, end, out value)) throw new InvalidDataException("Повреждён список игровых данных.");
                    yield return value;
                }
            }
        }

        internal string Text(int limit)
        {
            if (!Present || End - Start > limit * 4) return "";
            try
            {
                string value = Utf8.GetString(Bytes, Start, End - Start).Trim();
                return value.Length <= limit && value.IndexOf('\0') < 0 ? value : "";
            }
            catch (DecoderFallbackException) { return ""; }
        }
    }
}
