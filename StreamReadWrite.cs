using System;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using StreamUllrIO;

namespace StreamUllrIO
{
    public enum SeekFrom
    {
        Begin,
        Current,
        End,
    }

    public class UllrMemoryStream
    {
        private byte[] _data;
        private int _position;

        public UllrMemoryStream()
        {
            _data = Array.Empty<byte>();
            _position = 0;
        }

        public UllrMemoryStream(byte[] value)
        {
            _data = value;
            _position = 0;
        }

        public byte[] ToArray()
        {
            return (byte[])_data.Clone();
        }

        public int Seek(int offset, SeekFrom seekFrom)
        {
            switch (seekFrom)
            {
                case SeekFrom.Begin:
                    _position = offset;
                    break;
                case SeekFrom.Current:
                    _position += offset;
                    break;
                case SeekFrom.End:
                    _position = _data.Length + offset;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(seekFrom),
                        seekFrom,
                        null
                        );
            }
            
            if (_position < 0)
                _position = 0;
            if (_position > _data.Length)
                _position = _data.Length;

            return _position;
        }

        public byte[] Read(int count)
        {
            var bytesLeftToRead = _data.Length - _position;
            if (count > bytesLeftToRead)
            {
                count = bytesLeftToRead;
            }
            var bytes = new byte[count];
            Array.Copy(_data, _position, bytes, 0, count);

            _position += count;
            return bytes;
        }

        public void Write(byte[] value)
        {
            var requiredCapacity = _position + value.Length;
            if (_data.Length < requiredCapacity)
            {
                var newDataArray = new byte[requiredCapacity];
                Array.Copy(_data, newDataArray, _data.Length);
                _data = newDataArray;
            }
            Array.Copy(value, 0, _data, _position, value.Length);

            _position += value.Length;
        }
    }

    public static class UllrIO
    {
        public static int Seek(UllrMemoryStream s, int offset, SeekFrom seekFrom)
        {
            return s.Seek(offset, seekFrom);
        }
        
        public static byte[] ReadUnchecked(UllrMemoryStream s, int count)
        {
            return s.Read(count);
        }

        public static byte[] Read(UllrMemoryStream s, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            var result = ReadUnchecked(s, count);
            if (result.Length != count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    count,
                    null
                );
            }
            return result;
        }

        public static void Write(UllrMemoryStream s, byte[] value)
        {
           s.Write(value); 
        }

         // ------------------------------------------------------------
        // Byte
        // ------------------------------------------------------------

        public static byte ReadByte(UllrMemoryStream s)
        {
            return Read(s, 1)[0];
        }

        public static void WriteByte(
            UllrMemoryStream s,
            byte value)
        {
            Write(s, new[] { value });
        }

        // ------------------------------------------------------------
        // SByte
        // ------------------------------------------------------------

        public static sbyte ReadSByte(UllrMemoryStream s)
        {
            return unchecked((sbyte)ReadByte(s));
        }

        public static void WriteSByte(
            UllrMemoryStream s,
            sbyte value)
        {
            WriteByte(s, unchecked((byte)value));
        }

        // ------------------------------------------------------------
        // Bool
        // ------------------------------------------------------------

        public static bool ReadBool(UllrMemoryStream s)
        {
            return ReadByte(s) != 0;
        }

        public static void WriteBool(
            UllrMemoryStream s,
            bool value)
        {
            WriteByte(s, value ? (byte)1 : (byte)0);
        }

        // ------------------------------------------------------------
        // Short
        // ------------------------------------------------------------

        public static short ReadShort(UllrMemoryStream s)
        {
            var bytes = Read(s, 2);
            FixEndianess(bytes);
            return BitConverter.ToInt16(bytes);
        }

        public static void WriteShort(
            UllrMemoryStream s,
            short value)
        {
            var bytes = BitConverter.GetBytes(value);
            FixEndianess(bytes);
            Write(s, bytes);
        }

        // ------------------------------------------------------------
        // UShort
        // ------------------------------------------------------------

        public static ushort ReadUShort(UllrMemoryStream s)
        {
            var bytes = Read(s, 2);
            FixEndianess(bytes);
            return BitConverter.ToUInt16(bytes);
        }

        public static void WriteUShort(
            UllrMemoryStream s,
            ushort value)
        {
            var bytes = BitConverter.GetBytes(value);
            FixEndianess(bytes);
            Write(s, bytes);
        }

        // ------------------------------------------------------------
        // Int
        // ------------------------------------------------------------

        public static int ReadInt(UllrMemoryStream s)
        {
            var bytes = Read(s, 4);
            FixEndianess(bytes);
            return BitConverter.ToInt32(bytes);
        }

        public static void WriteInt(
            UllrMemoryStream s,
            int value)
        {
            var bytes = BitConverter.GetBytes(value);
            FixEndianess(bytes);
            Write(s, bytes);
        }

        // ------------------------------------------------------------
        // UInt
        // ------------------------------------------------------------

        public static uint ReadUInt(UllrMemoryStream s)
        {
            var bytes = Read(s, 4);
            FixEndianess(bytes);
            return BitConverter.ToUInt32(bytes);
        }

        public static void WriteUInt(
            UllrMemoryStream s,
            uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            FixEndianess(bytes);
            Write(s, bytes);
        }

        // ------------------------------------------------------------
        // Long
        // ------------------------------------------------------------

        public static long ReadLong(UllrMemoryStream s)
        {
            var bytes = Read(s, 8);
            FixEndianess(bytes);
            return BitConverter.ToInt64(bytes);
        }

        public static void WriteLong(
            UllrMemoryStream s,
            long value)
        {
            var bytes = BitConverter.GetBytes(value);
            FixEndianess(bytes);
            Write(s, bytes);
        }

        // ------------------------------------------------------------
        // ULong
        // ------------------------------------------------------------

        public static ulong ReadULong(UllrMemoryStream s)
        {
            var bytes = Read(s, 8);
            FixEndianess(bytes);
            return BitConverter.ToUInt64(bytes);
        }

        public static void WriteULong(
            UllrMemoryStream s,
            ulong value)
        {
            var bytes = BitConverter.GetBytes(value);
            FixEndianess(bytes);
            Write(s, bytes);
        }

        // ------------------------------------------------------------
        // Float
        // ------------------------------------------------------------

        public static float ReadFloat(UllrMemoryStream s)
        {
            var bytes = Read(s, 4);
            FixEndianess(bytes);
            return BitConverter.ToSingle(bytes);
        }

        public static void WriteFloat(
            UllrMemoryStream s,
            float value)
        {
            var bytes = BitConverter.GetBytes(value);
            FixEndianess(bytes);
            Write(s, bytes);
        }

        // ------------------------------------------------------------
        // Double
        // ------------------------------------------------------------

        public static double ReadDouble(UllrMemoryStream s)
        {
            var bytes = Read(s, 8);
            FixEndianess(bytes);
            return BitConverter.ToDouble(bytes);
        }

        public static void WriteDouble(
            UllrMemoryStream s,
            double value)
        {
            var bytes = BitConverter.GetBytes(value);
            FixEndianess(bytes);
            Write(s, bytes);
        }

        // ------------------------------------------------------------
        // Char
        // ------------------------------------------------------------

        public static char ReadChar(UllrMemoryStream s)
        {
            var bytes = Read(s, 2);
            FixEndianess(bytes);
            return BitConverter.ToChar(bytes);
        }

        public static void WriteChar(
            UllrMemoryStream s,
            char value)
        {
            var bytes = BitConverter.GetBytes(value);
            FixEndianess(bytes);
            Write(s, bytes);
        }
        
        // ------------------------------------------------------------
        //  Enum
        // ------------------------------------------------------------
        
        public static T ReadEnum<T>(UllrMemoryStream s) where T : Enum
        {
            var i = ReadInt(s);
            if(!Enum.IsDefined(typeof(T), i))
            {
                throw new FormatException(
                    $"{i} is not defined for enum {typeof(T)}"
                );
            }

            var result = (T)Enum.ToObject(typeof(T), i);
            return result;
        }

        // ------------------------------------------------------------
        //  String
        // ------------------------------------------------------------

        public static string ReadString(UllrMemoryStream s)
        {
            var length = ReadInt(s);
            var bytes = Read(s, length);
            var result = Encoding.UTF8.GetString(bytes);
            return result;
        }

        public static void WriteString(UllrMemoryStream s, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteInt(s, bytes.Length);
            Write(s, bytes);
        }

        // ------------------------------------------------------------
        //  Class and Struct
        // ------------------------------------------------------------

        public static void Serialize(UllrMemoryStream s, object value)
        {
            if(value == null)
            {
                WriteBool(s, false);
                return;
            }

            SerializeValue(s, value, value.GetType());
        }

        public static void SerializeValue(UllrMemoryStream s, object value, Type type)
        {

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            Type underlyingType = Nullable.GetUnderlyingType(type);
            
            if(IsNullableType(type, underlyingType))
            {
                bool hasValue = value !=null;
                WriteBool(s, hasValue);
                if (!hasValue)
                    return;

                if(underlyingType != null)
                {
                    type = underlyingType;
                }
                
            }

            // Primitive types
            if (type == typeof(byte))
            {
                WriteByte(s, (byte)value);
                return;
            }

            if (type == typeof(sbyte))
            {
                WriteSByte(s, (sbyte)value);
                return;
            }

            if (type == typeof(bool))
            {
                WriteBool(s, (bool)value);
                return;
            }

            if (type == typeof(short))
            {
                WriteShort(s, (short)value);
                return;
            }

            if (type == typeof(ushort))
            {
                WriteUShort(s, (ushort)value);
                return;
            }

            if (type == typeof(int))
            {
                WriteInt(s, (int)value);
                return;
            }

            if (type == typeof(uint))
            {
                WriteUInt(s, (uint)value);
                return;
            }

            if (type == typeof(long))
            {
                WriteLong(s, (long)value);
                return;
            }

            if (type == typeof(ulong))
            {
                WriteULong(s, (ulong)value);
                return;
            }

            if (type == typeof(float))
            {
                WriteFloat(s, (float)value);
                return;
            }

            if (type == typeof(double))
            {
                WriteDouble(s, (double)value);
                return;
            }

            if (type == typeof(char))
            {
                WriteChar(s, (char)value);
                return;
            }

            // Enum
            if (type.IsEnum)
            {
                WriteInt(s, Convert.ToInt32(value));
                return;
            }

            //String
            if (type == typeof(string))
            {
                WriteString(s, (string)value);
                return;
            }

            //Array
            if (type.IsArray)
            {
                SerializeArray(s, (Array)value, type.GetElementType());
                return;
            }

            //List<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                SerializeList(s, value, type);
                return;
            }

            // Struct/class
            SerializeFields(s, value, type);
        }

        private static void SerializeArray(UllrMemoryStream s, Array array, Type type)
        {
            WriteInt(s, array.Length);

            for (int i = 0; i < array.Length; i++)
            {
                var value = array.GetValue(i);
                if (value == null)
                    throw new ArgumentNullException($"Array element at index {i} is null");
                
                SerializeValue(s, value, type);
            }
        }

        private static void SerializeList(UllrMemoryStream s, object value, Type type)
        {
            var elementType = type.GetGenericArguments()[0];

            var list = (System.Collections.IList)value;

            WriteInt(s, list.Count);

            for (int i = 0; i < list.Count; i++)
            {
                var element = list[i];

                if (element == null)
                    throw new ArgumentNullException($"List element at index {i} is null");

                SerializeValue(s, element, elementType);
            }
        }

        private static void SerializeFields(UllrMemoryStream s, object value, Type type)
        {
            var fields = type.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic 
            );

            foreach (var field in fields)
            {
                if (field.IsStatic)
                    continue;

                var fieldValue = field.GetValue(value);

                SerializeValue(s, fieldValue, field.FieldType);
            }
        }

        public static T Deserialize<T>(UllrMemoryStream s)
        {
            return (T)DeserializeValue(s, typeof(T));
        }

        private static object DeserializeValue(UllrMemoryStream s, Type type)
        {

            Type underlyingType = Nullable.GetUnderlyingType(type);
            if(IsNullableType(type, underlyingType))
            {
                bool hasValue = ReadBool(s);
                if(!hasValue)
                    return null;
                if (underlyingType!=null)
                    type = underlyingType;
            }

            if (type == typeof(byte))
                return ReadByte(s);

            if (type == typeof(sbyte))
                return ReadSByte(s);

            if (type == typeof(bool))
                return ReadBool(s);

            if (type == typeof(short))
                return ReadShort(s);

            if (type == typeof(ushort))
                return ReadUShort(s);

            if (type == typeof(int))
                return ReadInt(s);

            if (type == typeof(uint))
                return ReadUInt(s);

            if (type == typeof(long))
                return ReadLong(s);

            if (type == typeof(ulong))
                return ReadULong(s);

            if (type == typeof(float))
                return ReadFloat(s);

            if (type == typeof(double))
                return ReadDouble(s);

            if (type == typeof(char))
                return ReadChar(s);

            if (type == typeof(string))
                return ReadString(s);

            if (type.IsEnum)
            {
                var value = ReadInt(s);
                return Enum.ToObject(type, value);
            }

            if (type.IsArray)
                return DeserializeArray(s, type.GetElementType());

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return DeserializeList(s, type);

            return DeserializeFields(s, type);
        }

        private static Array DeserializeArray(UllrMemoryStream s, Type arrayType)
        {
            var length = ReadInt(s);

            if (length < 0)
                throw new FormatException($"Invalid array length: {length}");

            var array = Array.CreateInstance(arrayType, length);

            for (int i = 0;  i < length; i++)
            {
                var value = DeserializeValue(s, arrayType);
                array.SetValue(value, i);
            }

            return array;
        }

        private static object DeserializeList(UllrMemoryStream s, Type listType)
        {
            var elementType = listType.GetGenericArguments()[0];

            var count = ReadInt(s);

            if (count < 0)
                throw new FormatException($"Invalid array length: {count}");

            var list = (System.Collections.IList) Activator.CreateInstance(listType);

            for (int i = 0; i < count; i++)
            {
                var element = DeserializeValue(s, elementType);
                list.Add(element);
            }

            return list;
        }

        private static object DeserializeFields(UllrMemoryStream s, Type type)
        {
            var instance = Activator.CreateInstance(type);

            var fields = type.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic
            );

            foreach (var field in fields)
            {
                if (field.IsStatic)
                    continue;
                
                var fieldValue = DeserializeValue(s, field.FieldType);

                field.SetValue(instance, fieldValue);
            }
            return instance;
        }

        public static void FixEndianess(byte[] value)
        {
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(value);
        }

        private static bool IsNullableType(Type type, Type underlyingType)
        {
            return !type.IsValueType || underlyingType != null;
        }
    }
}
