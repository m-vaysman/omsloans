using AutoMapper;
using DevExpress.Xpf.Grid;
using DevExpress.Xpf.Grid.Native;
using LoanDbModel;
using OMS.Loans.ViewModels;

namespace OMS.Loans.Mapping
{
    public class MappingProfile
    {
        public IMapper Mapper { get; }
        public MappingProfile()
        {
            var configuration = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Blotter, BlotterItem>()
                .ForMember("counterPartyName", a => a.Ignore())

             .ForMember(dest => dest.CounterPartyName,
                        opt => opt.MapFrom(src => src.CounterParty != null ? src.CounterParty.CounterPartyName : string.Empty))
             .ForMember(dest => dest.Ticker, opt => opt.MapFrom(src => src.Ticker))
             .ForMember(dest => dest.CounterPartyId, opt => opt.MapFrom(src => src.CounterPartyId))
             .ForMember(dest => dest.Cusip, opt => opt.MapFrom(src => src.CUSIP))
             .ForMember(dest => dest.TradeDate, opt => opt.MapFrom(src => src.TradeDate))
             .ForMember(dest => dest.BuySell, opt => opt.MapFrom(src => src.BuySell))
             .ForMember(dest => dest.Price, opt => opt.MapFrom(src => src.Price))
             .ForMember(dest => dest.GlobalCommitment, opt => opt.MapFrom(src => src.GlobalCommitment))
             .ForMember(dest => dest.Notional, opt => opt.MapFrom(src => src.Notional))
             .ForMember(dest => dest.TradeAcct, opt => opt.MapFrom(src => src.TradeAcct))
             .ForMember(dest => dest.Document, opt => opt.MapFrom(src => src.Document))
             .ForMember(dest => dest.Ticket, opt => opt.MapFrom(src => src.Ticket))
             .ForMember(dest => dest.Spread, opt => opt.MapFrom(src => src.Spread));

                cfg.CreateMap<BlotterItem, Blotter>()
                .ForMember("BlotterId", a => a.Ignore())
            .ForMember(dest => dest.CounterPartyId, opt => opt.MapFrom(src => src.CounterPartyId))
            .ForMember(dest => dest.CUSIP, opt => opt.MapFrom(src => src.Cusip))
            .ForMember(dest => dest.TradeDate, opt => opt.MapFrom(src => src.TradeDate))
            .ForMember(dest => dest.BuySell, opt => opt.MapFrom(src => src.BuySell))
            .ForMember(dest => dest.Price, opt => opt.MapFrom(src => src.Price))
            .ForMember(dest => dest.GlobalCommitment, opt => opt.MapFrom(src => src.GlobalCommitment))
            .ForMember(dest => dest.Notional, opt => opt.MapFrom(src => src.Notional))
            .ForMember(dest => dest.TradeAcct, opt => opt.MapFrom(src => src.TradeAcct))
            .ForMember(dest => dest.Document, opt => opt.MapFrom(src => src.Document))
            .ForMember(dest => dest.Ticket, opt => opt.MapFrom(src => src.Ticket))
            .ForMember(dest => dest.Spread, opt => opt.MapFrom(src => src.Spread))
            .ForMember(dest => dest.Ticker, opt => opt.MapFrom(src => src.Ticker))

            // Ignore CounterParty object – you can load it from DB separately
            .ForMember(dest => dest.CounterParty, opt => opt.Ignore());



                cfg.CreateMap<Trade, TradeEntryItem>()
              
                .ForMember(dest => dest.Paydowns, opt => opt.Ignore());
                
                

                // TradeModel → Trade
                cfg.CreateMap<TradeEntryItem, Trade>()
                .ForMember(dest => dest.TradeDocuments, opt => opt.Ignore())
                .ForMember(dest=>dest.Accruals,opt=>opt.Ignore())
                
                     .ForMember(dest => dest.Paydowns, opt => opt.Ignore());



                // Use constructor binding for AccrualCode
                cfg.CreateMap<Accrual, AccrualEntryItemViewModel>()
                    .ConstructUsing(src => new AccrualEntryItemViewModel(src.AccrualCode))
                   .ForMember(dest=>dest.ExpectedCash, opt=>opt.Ignore())
                    .ForMember(dest => dest.AccrualId, opt => opt.MapFrom(src => src.AccrualId))
                    .ForMember(dest => dest.ParentAccrualId, opt => opt.MapFrom(src => src.ParentAccrualId))
                    .ForMember(dest => dest.TradeId, opt => opt.MapFrom(src => src.TradeId))
                    .ForMember(dest => dest.Notional, opt => opt.MapFrom(src => src.Notional))
                    .ForMember(dest => dest.FromDate, opt => opt.MapFrom(src => src.FromDate))
                    .ForMember(dest => dest.ToDate, opt => opt.MapFrom(src => src.ToDate))
                    .ForMember(dest => dest.BankRate, opt => opt.MapFrom(src => src.BankRate))
                    .ForMember(dest => dest.Spread, opt => opt.MapFrom(src => src.Spread))
                    .ForMember(dest => dest.Act, opt => opt.MapFrom(src => src.Act))
                    .ForMember(dest => dest.Note, opt => opt.MapFrom(src => src.Note));

                cfg.CreateMap<AccrualEntryItemViewModel, Accrual>()
               
                .ForMember(dest=>dest.ChildAccruals,opt=>opt.Ignore())
                .ForMember(dest=>dest.ParentAccrual, opt=>opt.Ignore())
                .ForMember(dest=>dest.Trade, opt=>opt.Ignore())
          .ForMember(dest => dest.AccrualCode, opt => opt.Ignore()); // Read-only/computed
            });

            // only during development, validate your mappings; remove it before release
#if DEBUG
            configuration.AssertConfigurationIsValid();

#endif
            // use DI (http://docs.automapper.org/en/latest/Dependency-injection.html) or create the mapper yourself
            var mapper = configuration.CreateMapper();
            Mapper = mapper;
        }


    }
}
