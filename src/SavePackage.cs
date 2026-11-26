using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace SimsLegacy
{
    internal static class SavePackage
    {
        private const uint SaveGameData = 13;
        private const uint ExtendedSize = 0x80000000;
        private const int MaxResource = 384 * 1024 * 1024;

        public static IEnumerable<byte[]> ReadGameData(string path, FileShare share)
        {
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, share))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 96 || reader.ReadUInt32() != 0x46504244 || reader.ReadUInt32() != 2)
                    throw new InvalidDataException("Выберите файл сохранения The Sims 4 в формате .save.");
                stream.Position = 36;
                uint count = reader.ReadUInt32();
                uint oldOffset = reader.ReadUInt32();
                uint indexSize = reader.ReadUInt32();
                stream.Position = 64;
                ulong offset = reader.ReadUInt64();
                if (offset == 0) offset = oldOffset;
                if (count == 0 || count > 1000000 || indexSize < 4 ||
                    offset > (ulong)stream.Length || (ulong)indexSize > (ulong)stream.Length - offset)
                    throw new InvalidDataException("Повреждён индекс сохранения.");
                stream.Position = (long)offset;
                long end = stream.Position + indexSize;
                uint flags = reader.ReadUInt32();
                if ((flags & ~7u) != 0) throw new InvalidDataException("Версия индекса сохранения не поддерживается.");
                uint? type = (flags & 1) != 0 ? (uint?)reader.ReadUInt32() : null;
                if ((flags & 2) != 0) reader.ReadUInt32();
                if ((flags & 4) != 0) reader.ReadUInt32();
                int entrySize = 28 - ((flags & 1) != 0 ? 4 : 0) -
                    ((flags & 2) != 0 ? 4 : 0) - ((flags & 4) != 0 ? 4 : 0);
                long total = 0;
                for (int i = 0; i < count; i++)
                {
                    if (end - stream.Position < entrySize) throw new InvalidDataException("Обрывается индекс сохранения.");
                    uint resourceType = type ?? reader.ReadUInt32();
                    if ((flags & 2) == 0) reader.ReadUInt32();
                    if ((flags & 4) == 0) reader.ReadUInt32();
                    reader.ReadUInt32();
                    uint position = reader.ReadUInt32();
                    uint packed = reader.ReadUInt32();
                    uint size = reader.ReadUInt32();
                    ushort compression = 0;
                    if ((packed & ExtendedSize) != 0)
                    {
                        if (end - stream.Position < 4) throw new InvalidDataException("Обрывается индекс сохранения.");
                        compression = reader.ReadUInt16();
                        reader.ReadUInt16();
                    }
                    if (resourceType != SaveGameData || compression == 0xffe0) continue;
                    packed &= ~ExtendedSize;
                    total += Math.Max(size, packed);
                    if (position < 96 || (long)position + packed > stream.Length || packed == 0 ||
                        packed > MaxResource || size > MaxResource || total > MaxResource)
                        throw new InvalidDataException("Данные сохранения повреждены или слишком велики.");
                    long resume = stream.Position;
                    stream.Position = position;
                    var bytes = reader.ReadBytes((int)packed);
                    stream.Position = resume;
                    if (bytes.Length != packed) throw new InvalidDataException("Файл сохранения прочитан не полностью.");
                    if (compression == 0xffff || compression == 0xfffe) bytes = RefPack(bytes, (int)size);
                    else if (compression == 0x5a42) bytes = Inflate(bytes, (int)size);
                    else if (compression != 0) throw new InvalidDataException("Тип сжатия сохранения не поддерживается.");
                    yield return bytes;
                }
            }
        }

        private static byte[] Inflate(byte[] source, int size)
        {
            if (source.Length < 6 || (source[0] & 15) != 8 || (source[1] & 32) != 0 ||
                (source[0] * 256 + source[1]) % 31 != 0)
                throw new InvalidDataException("Повреждён заголовок сжатых данных.");
            var result = new byte[size];
            using (var input = new MemoryStream(source, 2, source.Length - 6))
            using (var inflater = new DeflateStream(input, CompressionMode.Decompress))
            {
                int position = 0, count;
                while (position < size && (count = inflater.Read(result, position, size - position)) > 0) position += count;
                if (position != size || inflater.ReadByte() != -1) throw new InvalidDataException("Размер распакованных данных не совпадает.");
            }
            return result;
        }

        private static byte[] RefPack(byte[] source, int size)
        {
            int start = source.Length > 1 && source[1] == 0xfb ? 0 : 4;
            if (source.Length - start < 5 || source[start + 1] != 0xfb)
                throw new InvalidDataException("Не распознан заголовок RefPack.");
            int width = (source[start] & 0x80) != 0 ? 4 : 3;
            int input = start + 2 + ((source[start] & 1) != 0 ? width : 0);
            if (source.Length - input < width) throw new InvalidDataException("Обрывается заголовок RefPack.");
            uint length = 0;
            for (int i = 0; i < width; i++) length = (length << 8) | source[input++];
            if (length != size) throw new InvalidDataException("Размер RefPack не совпадает с индексом.");
            var result = new byte[size];
            int output = 0;
            while (input < source.Length)
            {
                int code = source[input++];
                int literals, copy = 0, distance = 0;
                if (code < 0x80)
                {
                    if (input >= source.Length) break;
                    literals = code & 3;
                    copy = ((code >> 2) & 7) + 3;
                    distance = ((code & 0x60) << 3) + source[input++] + 1;
                }
                else if (code < 0xc0)
                {
                    if (source.Length - input < 2) break;
                    int packed = source[input++];
                    literals = packed >> 6;
                    copy = (code & 63) + 4;
                    distance = ((packed & 63) << 8) + source[input++] + 1;
                }
                else if (code < 0xe0)
                {
                    if (source.Length - input < 3) break;
                    literals = code & 3;
                    distance = ((code & 16) << 12) + (source[input++] << 8) + source[input++] + 1;
                    copy = ((code & 12) << 6) + source[input++] + 5;
                }
                else literals = code < 0xfc ? ((code & 31) + 1) * 4 : code & 3;

                if (literals > source.Length - input || literals + copy > size - output) break;
                Buffer.BlockCopy(source, input, result, output, literals);
                input += literals;
                output += literals;
                if (distance > output) break;
                for (int i = 0; i < copy; i++, output++) result[output] = result[output - distance];
                if (code >= 0xfc && output == size) return result;
                if (code >= 0xfc) break;
            }
            throw new InvalidDataException("Обрывается сжатый блок сохранения.");
        }
    }
}
