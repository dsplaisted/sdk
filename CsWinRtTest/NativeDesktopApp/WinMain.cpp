#include "pch.h"

using namespace winrt;
using namespace Coords;
using namespace Windows::Foundation;

int __stdcall wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int)
{
    init_apartment(apartment_type::single_threaded);
    Uri uri(L"http://aka.ms/cppwinrt");
    ::MessageBoxW(::GetDesktopWindow(), uri.AbsoluteUri().c_str(), L"C++/WinRT Desktop Application", MB_OK);
    Coord a = Coord();
    Coord b = Coord(39.0, 80.0);
    ::MessageBoxW(::GetDesktopWindow(), uri.AbsoluteUri().c_str(), L"C++/WinRT Desktop Application", MB_OK);
    auto aStr = a.ToString();
    auto bStr = b.ToString();
    auto aToB = a.Distance(b);
}
