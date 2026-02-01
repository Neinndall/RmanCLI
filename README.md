# 🛠️ RiotManifestCore

**RiotManifestCore** is a professional-grade native CLI engine built in C# for high-speed interaction with Riot Games' manifests infrastructure. It provides a complete ecosystem for manifest discovery, real-time version tracking, and surgical asset acquisition for **League of Legends** and **Valorant**.

## ⚡ Key Features

*   **RMAN**: Specialized high-performance parser for Riot's `.rman` format, performing all operations directly in memory without external dependencies.
*   **Smart Scan**: Intelligent differential patching using **Blake3** hashing to verify local integrity and download only missing or modified chunks.
*   **Multi-Game Global Support**: Full automated discovery for League of Legends and Valorant across all regions (NA, EUW, LATAM, KR, BR, AP, PBE).
*   **Binary Version Extraction**: Automated logic to extract real build versions directly from executables (`LeagueClient.exe` & `VALORANT-Win64-Shipping.exe`) using binary pattern matching and metadata analysis.
*   **Fetch Versions**: Discovery process with parallel processing, capable of synchronizing a full global catalog.
*   **HUD Interface**: Clean, interactive CLI dashboard using Spectre.Console for real-time status tracking.

## 🚀 Getting Started

### Prerequisites

*   **[.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)** installed on your system.

## 📖 Usage

### 1. Catalog Discovery (Fetch)
Retrieve all latest manifest URLs and automatically extract real versions from Riot's binaries:
```powershell
.\RiotManifestCore.exe fetch --save "./Manifests"
```

### 2. Advanced Download / Patching
Download specific assets or patch an existing installation with regex filtering and high-speed multi-threading:
```powershell
.\RiotManifestCore.exe download <manifest_url> --output "C:/GameFiles" --langs en_US es_ES --threads 8
```

## 🛠️ Technical Commands

| Command | Alias | Description |
| :--- | :--- | :--- |
| `fetch` | `get` | Discovers latest manifests and extracts real versions from binaries. |
| `download` | `dl` | Patches game assets with high-speed differential engine. |

## ⚙️ Technical Specifications

*   **Framework**: .NET 10.0 (Windows x64)
*   **Networking**: HTTP/2 Multiplexing via `SocketsHttpHandler`
*   **Integrity**: High-speed **Blake3** & **XXHash64** validation
*   **Compression**: Native **Zstandard** support

---
*Developed with ❤️ for the research community.*
