﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IBApi;
using System.ComponentModel;
using System.Threading;
using System.Collections.Concurrent;
using System.Windows.Forms;
using Finance.Data;
using static Finance.Helpers;
using static Finance.Logger;

namespace Finance.LiveTrading
{
    public abstract class TradingAccountProvider : IProviderStatus
    {
        private static TradingProviderType CurrentProvider = TradingProviderType.NotSet;
        private static TradingAccountProvider _Instance { get; set; }
        public static TradingAccountProvider Instance
        {
            get
            {
                if (_Instance == null || CurrentProvider != Settings.Instance.TradingProvider)
                {
                    CurrentProvider = Settings.Instance.TradingProvider;
                    switch (CurrentProvider)
                    {
                        case TradingProviderType.NotSet:
                            throw new UnknownErrorException() { message = "No trading provider selected" };
                        case TradingProviderType.InteractiveBrokers:
                            _Instance = new IbkrTradingAccountProvider(Settings.Instance.IbkrTradingProviderPort, 2);
                            break;
                        default:
                            break;
                    }
                }
                return _Instance;
            }
        }

        #region Events

        public event TradingAccountIdListEventHandler TradingAccountIdList;
        protected void OnTradingAccountList(List<string> accountIds)
        {
            TradingAccountIdList?.Invoke(this, new TradingAccountListEventArgs(accountIds));
        }

        public event ActiveAccountChanged ActiveAccountChanged;
        protected void OnActiveAccountChanged()
        {
            ActiveAccountChanged?.Invoke(this, new AccountUpdateEventArgs(ActiveAccount));
            RequestAccountUpdate(ActiveAccount);
        }

        public event AccountUpdateEventHandler AccountInformationUpdate;
        protected void OnAccountUpdate(LiveAccount account)
        {
            AccountInformationUpdate?.Invoke(this, new AccountUpdateEventArgs(account));
        }

        public event TradeStatusUpdateEventHandler TradeStatusUpdate;
        protected void OnTradeStatusUpdate(LiveTrade trade)
        {
            TradeStatusUpdate?.Invoke(this, new TradeStatusUpdateEventArgs(trade));
        }

        public event OpenPositionEventHandler OpenPositionsUpdate;
        protected void OnOpenPositionUpdate(LivePosition position)
        {
            OpenPositionsUpdate?.Invoke(this, new OpenPositionEventArgs(position));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Status Indicator Control

        private ControlStatus _Status { get; set; } = ControlStatus.Offline;
        public ControlStatus Status
        {
            get => _Status;
            set
            {
                if (_Status != value)
                {
                    _Status = value;
                    OnPropertyChanged("Status");
                    StatusMessage = Status.Description();
                }
            }
        }

        private string _StatusMessage { get; set; } = "";
        public string StatusMessage
        {
            get
            {
                return _StatusMessage;
            }
            set
            {
                if (_StatusMessage != value)
                {
                    _StatusMessage = value;
                    OnPropertyChanged("StatusMessage");
                }
            }
        }

        private string _StatusMessage2 { get; set; } = "";
        public string StatusMessage2
        {
            get
            {
                return _StatusMessage2;
            }
            set
            {
                if (_StatusMessage2 != value)
                {
                    _StatusMessage2 = value;
                    OnPropertyChanged("StatusMessage");
                }
            }
        }

        #endregion

        private bool _Connected { get; set; } = false;
        public bool Connected
        {
            get
            {
                return _Connected;
            }
            protected set
            {
                if (_Connected != value)
                {
                    _Connected = value;
                    Status = (_Connected ? ControlStatus.Ready : ControlStatus.Offline);
                    OnPropertyChanged("Connected");
                }
            }
        }

        public abstract string Name { get; }

        public List<LiveAccount> AvailableAccounts { get; } = new List<LiveAccount>();

        private LiveAccount _ActiveAccount { get; set; }
        public LiveAccount ActiveAccount
        {
            get
            {
                return _ActiveAccount;
            }
            protected set
            {
                if (_ActiveAccount == value)
                    return;

                _ActiveAccount = value;
                OnActiveAccountChanged();
            }
        }

        protected TradingAccountProvider()
        {
        }

        public abstract void Connect();
        public abstract void Disconnect();

        public abstract void RequestAccountUpdate(LiveAccount account);
        public abstract void RequestAllPositions();

        public void SetActiveAccount(string accountId)
        {
            var acct = AvailableAccounts.Find(x => x.AccountId == accountId);
            if (acct == null)
            {
                Log(new LogMessage(ToString(), $"Could not load account {accountId}", LogMessageType.TradingError));
                return;
            }
            ActiveAccount = acct;
        }

    }

    public class IbkrTradingAccountProvider : TradingAccountProvider, EWrapper
    {

        public override string Name => "IBKR Trading";

        #region IBKR

        private static EClientSocket clientSocket;
        public readonly EReaderSignal signal;
        public int Port { get; }
        public int ClientId { get; }

        #endregion

        #region Account Information

        private bool TryGetAccount(string accountId, out IbkrAccount account)
        {
            var acct = AvailableAccounts.Find(x => x.AccountId == accountId);
            if (acct != null)
            {
                account = acct as IbkrAccount;
                return true;
            }
            else
            {
                account = null;
                return false;
            }
        }

        #endregion

        #region ID Numbers

        //
        // ID number used for submitting orders, initialized by IBKR callback initially
        //
        private int _NextOrderId { get; set; } = 0;
        private int NextOrderId
        {
            get
            {
                _NextOrderId += 5;
                return _NextOrderId;
            }
        }

        //
        // ID used for identifying client requests (internal)
        //
        private int _NextRequestId { get; set; } = 10000;
        private int NextRequestId
        {
            get
            {
                _NextRequestId += 5;
                return _NextRequestId;
            }
        }

        #endregion

        public IbkrTradingAccountProvider(int port, int clientId)
        {
            Port = port;
            ClientId = clientId;
            signal = new EReaderMonitorSignal();
            clientSocket = new EClientSocket(this, signal);
        }

        #region Connection Management

        public override void Connect()
        {
            if (!Connected)
            {
                Log(new LogMessage(ToString(), $"Connecting to IBKR Gateway on port {Port} (Trading Account)", LogMessageType.Production));
                clientSocket?.eConnect("localhost", Port, ClientId);
            }
        }
        public override void Disconnect()
        {
            if (Connected)
            {
                Log(new LogMessage(ToString(), "Disconnecting from IBKR Gateway (Trading Account)", LogMessageType.Production));
                clientSocket?.eDisconnect();
            }
        }

        #endregion

        public override void RequestAccountUpdate(LiveAccount account)
        {
            if (!Connected)
                Log(new LogMessage(ToString(), "Trade Provider Not Connected", LogMessageType.TradingError));

            Log(new LogMessage(ToString(), "Requesting Account Updates", LogMessageType.TradingSystemMessage));

            clientSocket.reqAccountUpdates(true, account.AccountId);
        }
        public override void RequestAllPositions()
        {

        }

        #region Trade Management

        public void SCRAM()
        {
            throw new NotImplementedException();
        }

        #endregion

        #region System

        public void error(Exception e)
        {
            Log(new LogMessage(ToString(), e.Message, LogMessageType.TradingError));
        }
        public void error(string str)
        {
            Log(new LogMessage(ToString(), str, LogMessageType.TradingError));
        }
        public void error(int id, int errorCode, string errorMsg)
        {
            if (id == -1)
            {
                // Not an error, a notification

                Log(new LogMessage(ToString(), $"{errorCode}: {errorMsg}", LogMessageType.TradingSystemMessage));
            }
            else
            {
                // Handle
                switch (errorCode)
                {
                    case 162: // Historical data cancelled
                    case 300:
                    case 366:
                        // Ignore
                        break;
                    default:
                        Log(new LogMessage(ToString(), $"Unhandled TWS Trading System Message ID {errorCode}: {errorMsg}", LogMessageType.TradingError));
                        break;
                }
            }
        }

        public void nextValidId(int orderId)
        {
            _NextOrderId = orderId;
        }

        public void connectAck()
        {
            try
            {
                if (Connected == clientSocket.IsConnected())
                    return;

                Connected = clientSocket.IsConnected();

                if (!Connected)
                    return;

                //Create a reader to consume messages from the TWS. The EReader will consume the incoming messages and put them in a queue
                EReader reader = new EReader(clientSocket, signal);
                reader.Start();

                //Once the messages are in the queue, an additional thread can be created to fetch them
                new Thread(() =>
                {
                    try
                    {
                        while (clientSocket.IsConnected())
                        {
                            signal.waitForSignal();
                            reader.processMsgs();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Exception in reader thread: {ex.Message}");
                    }
                })
                {
                    IsBackground = true
                }.Start();

            }
            catch (Exception ex)
            {
                Log(new LogMessage(ToString(), ex.Message, LogMessageType.SystemError));
                Console.WriteLine($"EXCEPTION:{GetCurrentMethod()}  {ex.Message}");
                return;
            }
        }
        public void connectionClosed()
        {
            try
            {
                Connected = clientSocket.IsConnected();
            }
            catch (Exception ex)
            {
                Log(new LogMessage(ToString(), ex.Message, LogMessageType.SystemError));
                Console.WriteLine($"EXCEPTION:{GetCurrentMethod()}  {ex.Message}");
                return;
            }
        }

        /// <summary>
        /// Receives a list of account numbers available to the user
        /// </summary>
        /// <param name="accountsList"></param>
        public void managedAccounts(string accountsList)
        {
            if (accountsList.Length == 0)
                return;

            AvailableAccounts.Clear();

            foreach (string accountNum in accountsList.Split(','))
            {
                if (AvailableAccounts.Exists(x => x.AccountId != accountNum))
                    continue;

                AvailableAccounts.Add(new IbkrAccount(accountNum));
                Log(new LogMessage(ToString(), $"Loaded account {accountNum}", LogMessageType.TradingSystemMessage));
            }

            Log(new LogMessage(ToString(), $"Loaded {AvailableAccounts.Count} IBKR accounts", LogMessageType.TradingNotification));
            OnTradingAccountList((from acct in AvailableAccounts select acct.AccountId).ToList());
        }

        #endregion

        #region Portfolio and Position Updates

        //
        // Used
        //
        public void updateAccountValue(string key, string value, string currency, string accountName)
        {
            if (!TryGetAccount(accountName, out var account))
                return;

            if (currency == "USD")
                account.SetAccountValue(key, value);
        }
        public void updatePortfolio(Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
        {
            if (!TryGetAccount(accountName, out IbkrAccount account))
                return;

            var pos = account.UpdatePosition<IbkrPosition>(contract.Symbol, position.ToDecimal(), averageCost.ToDecimal());

            Log(new LogMessage(ToString(), $"Updated position: {position:0.0} x {contract.Symbol} @ {averageCost:$0.00} in {accountName}", LogMessageType.TradingNotification));
        }

        //
        // Not Used
        //
        public void accountDownloadEnd(string account)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void accountSummary(int reqId, string account, string tag, string value, string currency)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void accountSummaryEnd(int reqId)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void updateAccountTime(string timestamp)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        #endregion

        public void orderStatus(int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
        {

            Console.WriteLine(GetCurrentMethod());
        }
        public void openOrder(int orderId, Contract contract, Order order, OrderState orderState)
        {

            Console.WriteLine(GetCurrentMethod());
        }
        public void openOrderEnd()
        {

            Console.WriteLine(GetCurrentMethod());
        }
        public void execDetails(int reqId, Contract contract, Execution execution)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void execDetailsEnd(int reqId)
        {

            Console.WriteLine(GetCurrentMethod()); ;
        }

        public void position(string account, Contract contract, double pos, double avgCost)
        {

            Console.WriteLine(GetCurrentMethod());
        }
        public void positionEnd()
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void positionMulti(int requestId, string account, string modelCode, Contract contract, double pos, double avgCost)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void positionMultiEnd(int requestId)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void accountUpdateMulti(int requestId, string account, string modelCode, string key, string value, string currency)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void accountUpdateMultiEnd(int requestId)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void pnl(int reqId, double dailyPnL, double unrealizedPnL, double realizedPnL)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void pnlSingle(int reqId, int pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void completedOrder(Contract contract, Order order, OrderState orderState)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void completedOrdersEnd()
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void realtimeBar(int reqId, long date, double open, double high, double low, double close, long volume, double WAP, int count)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void tickSize(int tickerId, int field, int size)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void tickString(int tickerId, int field, string value)
        {
            Console.WriteLine(GetCurrentMethod());
        }
        public void tickGeneric(int tickerId, int field, double value)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        #region Not Used

        public void orderBound(long orderId, int apiClientId, int apiOrderId)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void smartComponents(int reqId, Dictionary<int, KeyValuePair<string, char>> theMap)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void tickReqParams(int tickerId, double minTick, string bboExchange, int snapshotPermissions)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void newsProviders(NewsProvider[] newsProviders)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void newsArticle(int requestId, int articleType, string articleText)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void historicalNews(int requestId, string time, string providerCode, string articleId, string headline)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void historicalNewsEnd(int requestId, bool hasMore)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void headTimestamp(int reqId, string headTimestamp)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void histogramData(int reqId, HistogramEntry[] data)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void rerouteMktDataReq(int reqId, int conId, string exchange)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void rerouteMktDepthReq(int reqId, int conId, string exchange)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void marketRule(int marketRuleId, PriceIncrement[] priceIncrements)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void securityDefinitionOptionParameter(int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, HashSet<string> expirations, HashSet<double> strikes)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void securityDefinitionOptionParameterEnd(int reqId)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void softDollarTiers(int reqId, SoftDollarTier[] tiers)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void familyCodes(FamilyCode[] familyCodes)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void symbolSamples(int reqId, ContractDescription[] contractDescriptions)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void mktDepthExchanges(DepthMktDataDescription[] depthMktDataDescriptions)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void tickNews(int tickerId, long timeStamp, string providerCode, string articleId, string headline, string extraData)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void scannerParameters(string xml)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void scannerData(int reqId, int rank, ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void scannerDataEnd(int reqId)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void receiveFA(int faDataType, string faXmlData)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void verifyMessageAPI(string apiData)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void verifyCompleted(bool isSuccessful, string errorText)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void verifyAndAuthMessageAPI(string apiData, string xyzChallenge)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void verifyAndAuthCompleted(bool isSuccessful, string errorText)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void displayGroupList(int reqId, string groups)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void displayGroupUpdated(int reqId, string contractInfo)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void bondContractDetails(int reqId, ContractDetails contract)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void tickEFP(int tickerId, int tickType, double basisPoints, string formattedBasisPoints, double impliedFuture, int holdDays, string futureLastTradeDate, double dividendImpact, double dividendsToLastTradeDate)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void deltaNeutralValidation(int reqId, DeltaNeutralContract deltaNeutralContract)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void tickOptionComputation(int tickerId, int field, double impliedVolatility, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void tickSnapshotEnd(int tickerId)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void historicalTicks(int reqId, HistoricalTick[] ticks, bool done)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void historicalTicksLast(int reqId, HistoricalTickLast[] ticks, bool done)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void tickByTickMidPoint(int reqId, long time, double midPoint)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void contractDetails(int reqId, ContractDetails contractDetails)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void contractDetailsEnd(int reqId)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void commissionReport(CommissionReport commissionReport)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void fundamentalData(int reqId, string data)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void marketDataType(int reqId, int marketDataType)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void updateMktDepth(int tickerId, int position, int operation, int side, double price, int size)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void updateMktDepthL2(int tickerId, int position, string marketMaker, int operation, int side, double price, int size, bool isSmartDepth)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void updateNewsBulletin(int msgId, int msgType, string message, string origExchange)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void currentTime(long time)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void historicalData(int reqId, Bar bar)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void historicalDataUpdate(int reqId, Bar bar)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void historicalDataEnd(int reqId, string start, string end)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void historicalTicksBidAsk(int reqId, HistoricalTickBidAsk[] ticks, bool done)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void tickByTickAllLast(int reqId, int tickType, long time, double price, int size, TickAttribLast tickAttriblast, string exchange, string specialConditions)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, int bidSize, int askSize, TickAttribBidAsk tickAttribBidAsk)
        {
            Console.WriteLine(GetCurrentMethod());
        }

        #endregion
    }

}
