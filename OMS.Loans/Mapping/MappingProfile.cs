using AutoMapper;
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
