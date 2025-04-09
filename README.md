# OMS.Loans – WPF Loan Trade Entry System (Demo Project)

This is a demo for a future pet project **Order Management System (OMS)** for **Loan Trade Entry**, built using **WPF (.NET 8)** with a modular MVVM architecture. It showcases key enterprise design patterns, component separation, and UI responsiveness using **DevExpress**, **AutoMapper**, and **MVVM Toolkit**.
Currently built out first phase which is to capture Blottered. Trades.
This OMS should have Blotter to Trade. Trade Allocation. Events management(paydowns,drawdowns accruals, cash). Lastly it will have a module to match incoming cash with events. 


## 📸 UI Preview



![LoanTraderMainUi_Screenshot](Assets/LoanTraderMainUi.png)


![TradeEntry_Screenshot](Assets/TradeEntry.png)   --------> ![TradeEntry_Screenshot](Assets/TradeEntryFilled.png) 

Here’s a screenshot of the blotter view with DevExpress GridControl and trade entry fields:

The screen is a view composed of two other views a form for trade entry and a grid in a seperate view for blottered trade look up. The form has validation built in. It will not allow user to push trades with missing or wrong fields. 
The grid is searchable. It also allows user to easily book and replicate a blottered trade. Simply highlight a row and grid fills with trade data. 

![Blotter Screenshot](https://bitbucket.org/frfield/omsloans/raw/main/Assets/Blotter.PNG)

ACCRUAL ENTRY
![AccrualEntry Screenshot](Assets/AccrualEntry.png)

ACCRUAL ENTERED
![AccrualEntered Screenshot](Assets/AccrualEntered.png)

ACCRUAL SAVED
![AccrualSaved Screenshot](Assets/AccrualsSaved.png)

Deploy migration: simply change CONNECTION STRING in LoanDBContext.cs
protected override void OnConfiguring(DbContextOptionsBuilder options)
=> options.UseSqlServer(@"Data Source=YOUR_SERVER;User ID=YOUR_USER;Password=YOUR_PASSWORD;Initial Catalog=Oms;TrustServerCertificate=true;MultipleActiveResultSets=True;Max Pool Size=100;")


in project dir run 
   dotnet-ef migrations add InitialMigration
   
![Migrations Screenshot](https://bitbucket.org/frfield/omsloans/raw/main/Assets/Migrations.PNG)

---

## 🧰 Tech Stack

- **WPF (.NET 6)**
- **MVVM Toolkit** (`CommunityToolkit.Mvvm`)
- **DevExpress WPF UI Controls**
- **Entity Framework Core** (with migrations)
- **AutoMapper** (for ViewModel → Model mapping)
- **IoC/DI** (via .NET built-in container)
- **IDataErrorInfo** (validation support)
- **Message-based communication** (`ObservableRecipient`)

---

## 🗂️ Project Structure

### 📦 `LoanDbModel` (Class Library)
Encapsulates EF Core entities and DB context.

- **Entities:**
  - `Blotter.cs`
  - `Trade.cs`
  - `Cash.cs`
  - `CounterParty.cs`
  - `Accrual.cs`
  - `Paydown.cs`

- **EF Core Context & Migrations:**
  - `LoanDbContext.cs`
  - `/Migrations` folder contains all schema snapshots and updates

---

### 🖥️ `OMS.Loans` (WPF UI Project)

#### 🧩 Common
- Shared interfaces like `IBlotterEntries`, `ICounterParties`

#### 🔧 Mapping
- `MappingProfile.cs` – AutoMapper configuration for entity ↔ ViewModel transformations

#### 💬 Message
- MVVM messaging:
  - `BlotteredTradeSelected.cs`
  - `TradeBlotteredMessage.cs`

#### 🧪 Services
- `BlotterEntriesService.cs` – Service to manage blotter-related logic
- `CounterPartyService.cs` – Handles lookup and reference data

#### 🧠 ViewModels
Implements `ObservableRecipient`, validation (`IDataErrorInfo`), and MVVM logic.

- `BlotterEntriesViewModel.cs`
- `BlotterItem.cs` – Per-row VM
- `BlotterViewModel.cs`
- `MainViewModel.cs`

#### 🪟 Views
MVVM-bound XAML UI components using DevExpress controls.

- `BlotterEntriesView.xaml`
- `BlotterEntryView.xaml`
- `BlotterView.xaml`
- `MainWindow.xaml`

---

## 🚀 Features

- **Loan Trade Entry UI** with multi-field validation
- **Blotter view** showing trades using DevExpress grid
- **Real-time messaging** between components (selected trade messages, etc.)
- **Validation** using `IDataErrorInfo` per field
- **AutoMapper** mappings from DB entities to UI ViewModels
- **MVVM-first** with testable service and VM layers
- **Extensible architecture** for adding new trade types or UI modules

---

## 🔄 Getting Started

1. **Clone the repository**

   ```bash
   git clone <your-bitbucket-repo-url>
