# DragConvert

Konversi gambar di Windows dengan **tahan Shift + drag file** dari Explorer.
Menu lingkaran muncul di dekat kursor; lepas file di segmen format tujuan (PNG, JPG, JPEG, WEBP).
Hasil disimpan di folder yang sama dengan file asli.

## Prasyarat
- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Jalankan (mode dev)
```
cd DragConvert
dotnet run
```

## Build jadi satu file .exe
```
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Hasil: `bin\Release\net8.0-windows\win-x64\publish\DragConvert.exe`
(tidak butuh .NET terpasang di PC lain). Klik kanan ikon tray untuk "Mulai otomatis dengan Windows".

## Cara pakai
1. Jalankan DragConvert (ikon oranye muncul di system tray).
2. Di Explorer, tahan **Shift**, lalu drag satu atau beberapa file gambar.
3. Lepas di segmen format yang diinginkan.

## Menambah format
Edit array `Formats` di `OverlayWindow.xaml.cs` dan tambahkan case-nya di `ImageConverter.cs`.
