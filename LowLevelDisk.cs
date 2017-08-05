using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Management;
using System.Linq;

//==============================================
//   Разработчик: Баранов Олег Алексеевич
//   Резюме: https://www.dropbox.com/s/xk1fqakrbzr9mgj/Резюме+Баранов+Олег+Алексеевич.pdf?dl=0
//   Тел: +7 (917) 79-73-468
//   E-Mail: 03shein03@gmail.com
//==============================================

/// <summary>
/// Класс для работы с дисками на низком уровне.
/// ВНИМАНИЕ! Все действия производятся на Ваш страх и риск.
/// Разработчик не несет ответственности за причиненный ущерб, в результате работы программы с использованием данного класса.
/// </summary>
/// <permission>
/// Для работы с диском на низком уровне требуются права Администратора локального компьютера.
/// Установите в app.manifest уровень доступа level="requireAdministrator"
/// и запустите VS от имени Администратора.
/// </permission>
/// <remarks>
/// Данную библиотеку можно использовать для создания своей файловой системы,
/// программы для восстановления файлов, HEX редактора или просто для скрытой записи данных на диск.
/// </remarks>

public class LowLevelDisk
{

    #region "Импорт функций из библиотеки kernel32.dll для работы с диском на низком уровне"
    private const uint GENERIC_READ = 0x80000000u;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;

    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x8000000;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    private const uint FILE_FLAG_WRITE_THROUGH = 0x8000000;
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(string fileName, [MarshalAs(UnmanagedType.U4)]
    FileAccess fileAccess, [MarshalAs(UnmanagedType.U4)]
    FileShare fileShare, IntPtr securityAttributes, [MarshalAs(UnmanagedType.U4)]
    FileMode creationDisposition, int flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateFileA(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, ref uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static internal extern bool CloseHandle(IntPtr hObject);


    private SafeFileHandle ОбращениеКДрайверу;
    [StructLayout(LayoutKind.Sequential)]
    private struct DISK_EXTENT
    {
        internal int DiskNumber;
        internal long StartingOffset;
        internal long ExtentLength;
    }
    #endregion

    #region "Объявления переменых и параметров"

    private int НомерДиска = -1;
    private long _КолВоСекторовНаДиске = 0;
    public long КолВоСекторовНаДиске
    {
        get { return _КолВоСекторовНаДиске; }
    }


    private System.IO.FileStream ТекущийПотокВводаВывода;
    private string _ПутьКУстройству;
    public string ПутьКУстройству
    {
        get { return _ПутьКУстройству; }
    }
    #endregion

    #region "Инициализация"
    public LowLevelDisk(string ПутьКУстройству)
    {
        if (ПутьКУстройству == null && ПутьКУстройству.Trim().Length == 0)
        {
            return;
        }
        ЗадатьПутьКУстройству(ПутьКУстройству);
    }

    public LowLevelDisk(int ИндексДиска)
    {
        ЗадатьПутьКУстройству("\\\\.\\PHYSICALDRIVE" + ИндексДиска);
        if (ПутьКУстройству == null && ПутьКУстройству.Trim().Length == 0)
        {
            return;
        }
    }

    private void ЗадатьПутьКУстройству(string ПутьКУстройству)
    {
        _ПутьКУстройству = ПутьКУстройству;
        НомерДиска = ПолучитьНомерДиска();
        _КолВоСекторовНаДиске = Convert.ToInt64(ПолучитьРазмерДиска(ПутьКУстройству) / 512);
        СоздатьПотокВводаВывода();
    }
    #endregion

    #region "Базовые операции"
    private void СоздатьПотокВводаВывода()
    {
        try
        {
            ТекущийПотокВводаВывода.Close();
            ОбращениеКДрайверу.Close();
        }
        catch
        {
        }
        ОбращениеКДрайверу = CreateFileA(ПутьКУстройству, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_WRITE_THROUGH, IntPtr.Zero);
        ТекущийПотокВводаВывода = new System.IO.FileStream(ОбращениеКДрайверу, FileAccess.ReadWrite);
    }

    private void ЗакрытьПотокВводаВывода()
    {
        ТекущийПотокВводаВывода.Close();
    }


    public long ЗаписатьБайты(ref byte[] Данные, long ИндексCектора, long Смещение)
    {
        return ЗаписатьБайты(ref Данные, ИндексCектора * 512 + Смещение);
    }

    public long ЗаписатьБайты(ref byte[] Байты, long ИндексБайта)
	{
		long functionReturnValue = 0;
		//Запись осуществляется только по секторам,
		//поэтому приходится считывать целый сектор, изменять отдельные байты
		//и только потом можно записать получившийся сектор на диск.

		if (Байты == null || Байты.Length == 0) {
			return 0;
		}

		int КолВоСекторов = Convert.ToInt32(Math.Ceiling(Convert.ToDouble(Байты.Length / 512)));
		long Сектор = Convert.ToInt64(Math.Floor(Convert.ToDouble(ИндексБайта / 512)));
		long ИндексНедБайтыДо = Сектор * 512;
		long КолВоНедостающихБайтовДо = ИндексБайта - ИндексНедБайтыДо;

		long ИндексНедБайтыПосле = ИндексБайта + Байты.Length;

		long S_A = Convert.ToInt64(Math.Ceiling(Convert.ToDouble(ИндексНедБайтыПосле / 512)));
		long КолВоНедостающихБайтовПосле = S_A * 512 - ИндексНедБайтыПосле;

		byte[] БайтыДо = new byte[КолВоНедостающихБайтовДо];
        byte[] БайтыПосле = new byte[КолВоНедостающихБайтовПосле];

		if (КолВоНедостающихБайтовДо > 0) {
			БайтыДо = СчитатьБайты(ИндексНедБайтыДо, КолВоНедостающихБайтовДо);
		}

		if (КолВоНедостающихБайтовПосле > 0) {
			БайтыПосле = СчитатьБайты(ИндексНедБайтыПосле, КолВоНедостающихБайтовПосле);
		}

		byte[] Результат = БайтыДо.Concat(Байты).Concat(БайтыПосле).ToArray();

		S_A = 0;
		БайтыДо = null;
		БайтыПосле = null;
		ИндексБайта = 0;
		КолВоСекторов = 0;
		ИндексНедБайтыДо = 0;
		ИндексНедБайтыПосле = 0;
		КолВоНедостающихБайтовДо = 0;
		КолВоНедостающихБайтовПосле = 0;

		try {
			functionReturnValue = ЗаписатьСектор(ref Результат, Сектор);
		} catch (Exception ex) {
			throw new ApplicationException(ex.ToString());
		}
		return functionReturnValue;
	}




    public long ЗаписатьСектор(ref byte[] Байты, long ИндексСектораНаДиске)
    {
        long functionReturnValue = 0;
        try
        {
            СоздатьПотокВводаВывода();
            ТекущийПотокВводаВывода.Position = ИндексСектораНаДиске * 512;
            ТекущийПотокВводаВывода.Write(Байты, 0, Байты.Length);
            functionReturnValue = Байты.Length;
            Байты = null;
        }
        catch (Exception ex)
        {
            throw new ApplicationException(ex.ToString());
        }
        return functionReturnValue;
    }

    public byte[] СчитатьБайты(long ИндексСектора, long Смещение, long КолВо)
    {
        return СчитатьБайты(ИндексСектора * 512 + Смещение, КолВо);
    }

    public byte[] СчитатьБайты(long ИндексБайта, long КолВо = 512)
    {
        long ИндексПервогоСектора = Convert.ToInt64(Math.Floor(Convert.ToDouble( ИндексБайта / 512)));
        long ИндексПоследнегоСектора = Convert.ToInt64(Math.Floor(Convert.ToDouble((ИндексБайта + КолВо) / 512)));

        long КолВоСекторов = ИндексПоследнегоСектора - ИндексПервогоСектора + 1;
        byte[] МассивСекторов = null;
        try
        {
            МассивСекторов = СчитатьСектора(ИндексПервогоСектора, КолВоСекторов * 512);
            if (МассивСекторов == null)
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            throw new ApplicationException(ex.ToString());
        }

        int СмещениеВнутриСектора = Convert.ToInt32(ИндексБайта - Math.Floor(Convert.ToDouble(ИндексБайта / 512)) * 512);
        byte[] Результат = new byte[Convert.ToInt32(КолВо - 1) + 1];

        try
        {
            for (var i = 0; i <= КолВо - 1; i++)
            {
                Результат[Convert.ToInt32(i)] = МассивСекторов[Convert.ToInt32(СмещениеВнутриСектора + i)];
            }
        }
        catch (Exception ex)
        {
            throw new ApplicationException(ex.ToString());
        }

        СмещениеВнутриСектора = 0;
        МассивСекторов = null;
        КолВоСекторов = 0;
        ИндексПервогоСектора = 0;
        КолВо = 0;
        ИндексБайта = 0;
        return Результат;
    }

    public byte[] СчитатьСектора(long ИндексСектора, long КолВо = 1)
    {
        if ((ИндексСектора > КолВоСекторовНаДиске))
        {
            return null;
        }
        byte[] ReturnByte = new byte[Convert.ToInt32(КолВо * 512 - 1) + 1];
        try
        {
            СоздатьПотокВводаВывода();
            ТекущийПотокВводаВывода.Position = ИндексСектора * 512;
            ТекущийПотокВводаВывода.Read(ReturnByte, 0, Convert.ToInt32(КолВо * 512));
            ЗакрытьПотокВводаВывода();
        }
        catch (Exception ex)
        {
            throw new ApplicationException(ex.ToString());
        }
        return ReturnByte;
    }


    public static long ПолучитьРазмерДиска(string strDrive)
    {
        if (string.IsNullOrEmpty(strDrive) || strDrive == null)
        {
            return 0;
        }
        string Type = "";
        if (strDrive.ToUpper().IndexOf("PhysicalDrive".ToUpper()) > -1)
        {
            Type = "Win32_DiskDrive";
        }
        else
        {
            Type = "Win32_Volume";
            if (!strDrive.EndsWith("\\"))
            {
                strDrive += "\\";
            }
        }
        ManagementObject moHD = new ManagementObject(Type + ".DeviceID='" + strDrive + "'");
        moHD.Get();
        return Convert.ToInt64(moHD["Size"]);
    }

    public int ПолучитьНомерДиска()
    {
        int functionReturnValue = 0;
        functionReturnValue = -1;
        ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
        foreach (ManagementObject wmi_dd in searcher.Get())
        {
            if (wmi_dd["DeviceID"].ToString() == ПутьКУстройству)
            {
                functionReturnValue = Convert.ToInt32(wmi_dd["Index"].ToString());
                break;
            }
        }
        if (functionReturnValue == 0)
        {
            functionReturnValue = -1;
            System.Windows.Forms.MessageBox.Show("Ошибка! Не используйте системный диск!");
        }
        return functionReturnValue;
    }
    #endregion

    #region "Запись файлов"
    public long ЗаписатьФайлНаДиск(string ПутьКВходномуФайлу, long ИндексБайта)
    {
        long functionReturnValue = 0;
        try
        {
            System.IO.FileStream ВходнойПоток = new System.IO.FileStream(ПутьКВходномуФайлу, FileMode.Open, FileAccess.Read);

            int МаксРазмерБуфера = 4096;
            int ТекущийРазмерБуфера = МаксРазмерБуфера;
            byte[] Буфер = new byte[МаксРазмерБуфера];
            long РазмерСчитанногоБлока = 0;

            long КолВоБайтов = ВходнойПоток.Length;
            long ОставшиесяБайты = ВходнойПоток.Length;
            functionReturnValue = КолВоБайтов;

            long ИндексНачальногоСектора = Convert.ToInt64(Math.Floor(Convert.ToDouble(ИндексБайта / 512)));
            long ИндексКонечногоСектора = Convert.ToInt64(Math.Floor(Convert.ToDouble( (ИндексБайта + КолВоБайтов) / 512)));

            long КолВоСекторов = ИндексКонечногоСектора - ИндексНачальногоСектора + 1;
            int СмещениеВнутриСектора = Convert.ToInt32(ИндексБайта - ИндексНачальногоСектора * 512);


            //Если информация находится не в начале сектора
            if (СмещениеВнутриСектора > 0)
            {
                СоздатьПотокВводаВывода();
                ТекущийПотокВводаВывода.Position = ИндексБайта;
                Буфер = new byte[512 - СмещениеВнутриСектора];
                ВходнойПоток.Read(Буфер, 0, 512 - СмещениеВнутриСектора);
                ЗаписатьБайты(ref Буфер, ИндексБайта);
                ОставшиесяБайты -= Convert.ToInt64(Буфер.Length);
            }


            Буфер = new byte[МаксРазмерБуфера];
            ТекущийРазмерБуфера = МаксРазмерБуфера;

            СоздатьПотокВводаВывода();
            //Считываем данные целыми секторами
            while (ОставшиесяБайты > 4096)
            {
                РазмерСчитанногоБлока = ВходнойПоток.Read(Буфер, 0, ТекущийРазмерБуфера);
                ТекущийПотокВводаВывода.Position = ИндексБайта + КолВоБайтов - ОставшиесяБайты;
                ТекущийПотокВводаВывода.Write(Буфер, 0, Convert.ToInt32( РазмерСчитанногоБлока));
                ОставшиесяБайты -= Convert.ToInt64(РазмерСчитанногоБлока);
            }


            //Считываем данные с последнего сектора
            if (ОставшиесяБайты <= 4096)
            {
                СоздатьПотокВводаВывода();
                Буфер = new byte[Convert.ToInt32(ОставшиесяБайты - 1) + 1];
                ВходнойПоток.Read(Буфер, 0, Convert.ToInt32( ОставшиесяБайты));
                ЗаписатьБайты(ref Буфер, ИндексБайта + КолВоБайтов - ОставшиесяБайты);
                ОставшиесяБайты -= Convert.ToInt64(Буфер.Length);
            }

            ВходнойПоток.Close();

            ТекущийРазмерБуфера = 0;
            МаксРазмерБуфера = 0;
            Буфер = null;
            ОставшиесяБайты = 0;
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(ex.ToString());
            return 0;
        }
        return functionReturnValue;
    }

    public void СчитатьФайлСДиска(long ИндексБайта, long КолВоБайтов, string ПутьКВыходномуФайлу)
    {
        try
        {
            System.IO.FileStream ВыходнойПоток = new System.IO.FileStream(ПутьКВыходномуФайлу, FileMode.OpenOrCreate, FileAccess.Write);
            int МаксРазмерБуфера = 4096;
            int ТекущийРазмерБуфера = МаксРазмерБуфера;
            byte[] Буфер = new byte[МаксРазмерБуфера];
            long РазмерСчитанногоБлока = 0;
            long ОставшиесяБайты = КолВоБайтов;

            long ИндексПервогоСектора = Convert.ToInt64(Math.Floor( Convert.ToDouble( ИндексБайта / 512)));
            long ИндексПоследнегоСектора = Convert.ToInt64(Math.Floor(Convert.ToDouble((ИндексБайта + КолВоБайтов) / 512)));

            long КолВоСекторов = ИндексПоследнегоСектора - ИндексПервогоСектора + 1;
            int СмещениеВнутриСектора = Convert.ToInt32(ИндексБайта - ИндексПервогоСектора * 512);

            byte[] Результат = null;

            //Если информация находится не в начале сектора
            if (СмещениеВнутриСектора > 0)
            {
                СоздатьПотокВводаВывода();

                int СколькоСчитать = 0;
                if (СмещениеВнутриСектора + КолВоБайтов > 512)
                {
                    СколькоСчитать = 512 - СмещениеВнутриСектора;
                }
                else
                {
                    СколькоСчитать = Convert.ToInt32(КолВоБайтов);
                }

                Буфер = new byte[512];
                Результат = new byte[СколькоСчитать];

                ТекущийПотокВводаВывода.Position = ИндексБайта;
                ТекущийПотокВводаВывода.Read(Буфер, 0, 512);
                for (var i = 0; i <= СколькоСчитать - 1; i++)
                {
                    Результат[i] = Буфер[СмещениеВнутриСектора + i];
                }
                ВыходнойПоток.Write(Результат, 0, Результат.Length);
                ОставшиесяБайты -= Convert.ToInt64(Результат.Length);
            }

            Буфер = new byte[МаксРазмерБуфера];
            ТекущийРазмерБуфера = МаксРазмерБуфера;

            СоздатьПотокВводаВывода();
            //Считываем данные целыми секторами
            while (ОставшиесяБайты > ТекущийРазмерБуфера)
            {
                ТекущийПотокВводаВывода.Position = ИндексБайта + КолВоБайтов - ОставшиесяБайты;

                РазмерСчитанногоБлока = ТекущийПотокВводаВывода.Read(Буфер, 0, ТекущийРазмерБуфера);
                ВыходнойПоток.Write(Буфер, 0, Convert.ToInt32(РазмерСчитанногоБлока));
                ОставшиесяБайты -= Convert.ToInt64(РазмерСчитанногоБлока);

                if (ОставшиесяБайты < 4096 & ОставшиесяБайты > 3072)
                {
                    ТекущийРазмерБуфера = 3072;
                }
                else if (ОставшиесяБайты < 3072 & ОставшиесяБайты > 2048)
                {
                    ТекущийРазмерБуфера = 2048;
                }
                else if (ОставшиесяБайты < 2048 & ОставшиесяБайты > 1024)
                {
                    ТекущийРазмерБуфера = 1024;
                }
            }

            //Считываем данные с последнего сектора
            if (ОставшиесяБайты <= 1024)
            {
                СоздатьПотокВводаВывода();
                Буфер = СчитатьБайты(ИндексБайта + КолВоБайтов - ОставшиесяБайты, ОставшиесяБайты);
                ВыходнойПоток.Write(Буфер, 0, Буфер.Length);
                ОставшиесяБайты -= Convert.ToInt64(Буфер.Length);
            }

            ВыходнойПоток.Close();

            ТекущийРазмерБуфера = 0;
            МаксРазмерБуфера = 0;
            Буфер = null;
            ОставшиесяБайты = 0;
        }
        catch (Exception ex)
        {
            throw new ApplicationException(ex.ToString());
        }
    }
    #endregion

    #region "Обнуление диска"

    private static Random Рандом = new Random();
    public enum Заполнители
    {
        Нули,
        Рандом
    }

    private byte[] ЗаполнитьМассивРандомнымиЗначениями(int КолВоСекторов, Заполнители Заполнитель = Заполнители.Рандом)
    {
        byte[] Возврат = null;
        int РазмерСектора = 512;
        Возврат = new byte[КолВоСекторов * РазмерСектора];
        if (Заполнитель == Заполнители.Рандом)
        {
            Рандом.NextBytes(Возврат);
        }
        return Возврат;
    }

    public void ОбнулитьСектора(long НачальныйСектор, long КонечныйСектор, Заполнители Заполнитель = Заполнители.Рандом)
    {
        long КолВоСекторов = КонечныйСектор - НачальныйСектор;
        long КолВоСектровЗаПроход = 5000;
        long КолВоПроходов = Convert.ToInt64(Math.Floor( Convert.ToDouble( КолВоСекторов / КолВоСектровЗаПроход)));
        for (var i = 0; i <= КолВоПроходов - 1; i++)
        {
            byte[] Байты = ЗаполнитьМассивРандомнымиЗначениями(Convert.ToInt32(КолВоСектровЗаПроход), Заполнитель);
            ЗаписатьСектор(ref Байты, НачальныйСектор + i * КолВоСектровЗаПроход);
        }

        long КолВоОставшихсяСекторов = КолВоСекторов - КолВоПроходов * КолВоСектровЗаПроход;

        byte[] ОставшиесяБайты = ЗаполнитьМассивРандомнымиЗначениями(Convert.ToInt32(КолВоОставшихсяСекторов), Заполнитель);
        ЗаписатьСектор(ref ОставшиесяБайты, НачальныйСектор + КолВоПроходов * КолВоСектровЗаПроход);
    }

    public void ОбнулитьДиск()
    {
        ОбнулитьСектора(1, КолВоСекторовНаДиске - 1, Заполнители.Нули);
    }
    #endregion

}
