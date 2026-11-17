using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Mono.Data.Sqlite;

namespace TradeLedgerProbeB
{
    public interface ILedgerTradeHost
    {
        LedgerContext TradeContext { get; }
        void RecordTrade(LedgerTradeReceipt receipt);
    }
    public sealed class LedgerTradeReceipt
    {
        public string TradeId,Session,Utc,GameTicks,Action,ItemName,Merchant,MerchantSource,Total,Unit,Source,PaymentSource;
        public int PlayerId,ItemId,Auxiliary,Quantity;
        public LedgerContext Context;
        public LedgerIdentity Identity;
        public bool Confirmed;
    }
    // Captures native price-function returns, never changes stock/balances or executes game methods.
    public sealed class LedgerTradeProof
    {
        public bool PlayerBuys,MonetaryAccount,DeliverySucceeded,ReceiptSeen,ConflictingReceipt,Emitted;
        public int Requested,Actual,PriceQuantity,BuyQuantity;
        public float Factor,BuyCalculation,SellCalculation,AuditQuote;
        public bool HasBuy,HasSell;
        public int CashDebitCalls,CashDebitAmount,CashCreditCalls;
        public long CashCreditAmount;
        public bool CashDebitSucceeded,CashCreditFailed;
        public void CashDebit(int amount,bool succeeded)
        {CashDebitCalls++;CashDebitAmount=amount;CashDebitSucceeded=succeeded;}
        public void CashCredit(int amount,bool succeeded)
        {
            CashCreditCalls++;
            if(!succeeded||amount<=0||CashCreditCalls>256){CashCreditFailed=true;return;}
            CashCreditAmount+=amount;
        }
        public void Price(int quantity,bool buy,float result)
        {
            if(buy&&!HasBuy){HasBuy=true;BuyCalculation=result;BuyQuantity=quantity;if(PlayerBuys)PriceQuantity=quantity;}
            if(!buy&&!HasSell){HasSell=true;SellCalculation=result;PriceQuantity=quantity;}
        }
        public void Receipt(int actual,float quote)
        {if(ReceiptSeen&&(Actual!=actual||AuditQuote!=quote))ConflictingReceipt=true;ReceiptSeen=true;Actual=actual;AuditQuote=quote;}
        public bool Confirm(out string total,out string unit,out string reason)
        {
            total=unit=null;reason="native-outcome-incomplete";
            if(Emitted||!ReceiptSeen||ConflictingReceipt||Actual<=0||Actual>1000000||Requested<Actual||BuyQuantity!=Requested||!HasBuy||PriceQuantity!=Actual||!Finite(Factor)||!Finite(BuyCalculation)||!PlayerBuys&&!HasSell)return false;
            float buy=BuyCalculation*(1f-Factor), paid;
            if(AuditQuote!=buy){reason="audit-quote-fence";return false;}
            if(PlayerBuys){if(!DeliverySucceeded||Actual!=Requested){reason="item-delivery-unconfirmed";return false;}paid=buy;}
            else{paid=SellCalculation*(1f+Factor);if(buy>0)paid=Math.Min(paid,buy);}
            if(!Finite(paid)||paid<=0){reason="nonmonetary-or-invalid-amount";return false;}
            if(MonetaryAccount)
                // CB6C/CB6D promote this exact stored float to double in the bank account setter.
                total=((double)paid).ToString("R",CultureInfo.InvariantCulture);
            else
            {
                // Actual cash debit / successful native payout stack arguments, not a wallet delta.
                double nativeInteger=PlayerBuys?Math.Round((double)paid):Math.Truncate((double)paid);
                if(nativeInteger<=0||nativeInteger>int.MaxValue){reason="cash-amount-out-of-range";return false;}
                int expected=(int)nativeInteger;
                if(PlayerBuys)
                {
                    if(CashDebitCalls!=1||!CashDebitSucceeded||CashDebitAmount!=expected){reason="cash-debit-unconfirmed";return false;}
                    total=CashDebitAmount.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    if(CashCreditCalls==0||CashCreditFailed||CashCreditAmount!=expected){reason="cash-payout-unconfirmed";return false;}
                    total=CashCreditAmount.ToString(CultureInfo.InvariantCulture);
                }
            }
            decimal amount;if(!decimal.TryParse(total,NumberStyles.Float,CultureInfo.InvariantCulture,out amount)){reason="amount-unrepresentable";return false;}
            unit=(amount/Actual).ToString(CultureInfo.InvariantCulture);reason=null;return true;
        }
        public static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
    public sealed class LedgerTradeRow { public long Sequence;public LedgerTradeReceipt Receipt; }
    public sealed class LedgerTradePage { public List<LedgerTradeRow> Rows=new List<LedgerTradeRow>();public bool More; }
    public static class LedgerTradeReader
    {
        internal static LedgerTradePage Read(SqliteConnection db,SqliteTransaction tx,string world,int? player,long before=long.MaxValue)
        {
            var page=new LedgerTradePage();if(!(player>0))return page;
            using(var c=db.CreateCommand())
            {
                c.Transaction=tx;c.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='trades_v1'";
                if(Convert.ToInt32(c.ExecuteScalar())==0)return page;
                c.CommandText="SELECT sequence,receipt_json FROM trades_v1 WHERE world_key=@world AND player_id=@player AND sequence<@before ORDER BY sequence DESC LIMIT 257";
                c.Parameters.AddWithValue("@world",world);c.Parameters.AddWithValue("@player",player.Value);c.Parameters.AddWithValue("@before",before);
                using(var r=c.ExecuteReader())while(r.Read())
                {
                    if(page.Rows.Count==256){page.More=true;break;}
                    var bytes=(byte[])r[1];if(bytes.Length>32768)throw new InvalidDataException("trade-receipt-size");
                    var receipt=LedgerJson.Decode<LedgerTradeReceipt>(bytes);
                    if(!LedgerTradePolicy.Valid(receipt)||receipt.Context.WorldKey!=world||receipt.PlayerId!=player)throw new InvalidDataException("trade-receipt-scope");
                    page.Rows.Add(new LedgerTradeRow{Sequence=(long)r[0],Receipt=receipt});
                }
            }
            return page;
        }
        public static LedgerTradePage Older(string path,string world,int player,long before)
        {
            LedgerSqliteRuntime.Prepare();
            using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,ReadOnly=true,FailIfMissing=true,Pooling=false,Version=3,DefaultTimeout=1}.ToString()))
            {
                db.Open();using(var c=db.CreateCommand()){c.CommandText="PRAGMA query_only=ON";c.ExecuteNonQuery();c.CommandText="PRAGMA application_id";if(Convert.ToInt32(c.ExecuteScalar())!=1414288177)throw new InvalidDataException("not-owned-ledger");}
                using(var tx=db.BeginTransaction(System.Data.IsolationLevel.ReadCommitted)){var result=Read(db,tx,world,player,before);tx.Commit();return result;}
            }
        }
    }
    public static class LedgerTradePolicy
    {
        public static bool Valid(LedgerTradeReceipt r)
        {
            decimal total,unit;DateTime time;
            return r!=null&&r.Confirmed&&r.Context!=null&&r.Context.Recordable&&r.Context.Mode=="SinglePlayer"&&!string.IsNullOrEmpty(r.Context.WorldKey)&&!string.IsNullOrEmpty(r.Context.Playfield)&&r.PlayerId>0&&r.Identity!=null&&r.Identity.NpcEntityId>0&&r.Identity.OwnerConsistent!=false&&r.ItemId>0&&r.Quantity>0&&r.Quantity<=1000000&&(r.Action=="Bought"||r.Action=="Sold")&&!string.IsNullOrEmpty(r.TradeId)&&r.TradeId.Length<=80&&DateTime.TryParse(r.Utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out time)&&!string.IsNullOrEmpty(r.Source)&&decimal.TryParse(r.Total,NumberStyles.Float,CultureInfo.InvariantCulture,out total)&&total>0&&decimal.TryParse(r.Unit,NumberStyles.Float,CultureInfo.InvariantCulture,out unit)&&unit>0;
        }
    }
    public sealed partial class LedgerViewData
    {
        public string TradeVendor(LedgerTradeReceipt r)
        {
            if(!string.IsNullOrEmpty(r.Merchant))return r.Merchant;
            var source=new LedgerObservation{Context=r.Context,Identity=r.Identity};LedgerEndpointState known;
            if(r.Context==null||r.Context.WorldKey!=WorldKey||r.Identity==null||r.Identity.OwnerConsistent==false||!endpoints.TryGetValue(LedgerDatabase.Key(source),out known)||known.Identity==null||known.Identity.OwnerConsistent==false)return "Unknown merchant";
            var a=r.Identity;var b=known.Identity;
            if(a.StructureEntityId.HasValue&&b.StructureEntityId.HasValue&&a.StructureEntityId!=b.StructureEntityId||a.BlockGrid!=null&&b.BlockGrid!=null&&a.BlockGrid!=b.BlockGrid)return "Unknown merchant";
            return known.LastDialogue==null||known.LastDialogue.Dialogue==null?"Unknown merchant":known.LastDialogue.Dialogue.TitleVerbatim??"Unknown merchant";
        }
    }
    public sealed partial class LedgerDatabase
    {
        public bool SaveTrade(LedgerTradeReceipt r)
        {
            if(!LedgerTradePolicy.Valid(r))throw new InvalidDataException("unconfirmed-or-invalid-trade");
            byte[] bytes=LedgerJson.Encode(r);if(bytes.Length>32768)throw new InvalidDataException("trade-receipt-size");
            try
            {
                using(transaction=connection.BeginTransaction())
                {
                    Exec(@"CREATE TABLE IF NOT EXISTS trades_v1(sequence INTEGER PRIMARY KEY AUTOINCREMENT,trade_id TEXT NOT NULL UNIQUE,world_key TEXT NOT NULL,player_id INTEGER NOT NULL,observed_utc TEXT NOT NULL,receipt_json BLOB NOT NULL);CREATE INDEX IF NOT EXISTS trades_scope_v1 ON trades_v1(world_key,player_id,sequence);");
                    var prior=Scalar("SELECT receipt_json FROM trades_v1 WHERE trade_id=@p0",r.TradeId) as byte[];
                    if(prior!=null){if(LedgerJson.Hash(prior)!=LedgerJson.Hash(bytes))throw new InvalidDataException("trade-id-conflict");return false;}
                    if(string.IsNullOrEmpty(r.Merchant))
                    {
                        var source=new LedgerObservation{Context=r.Context,Identity=r.Identity};
                        var endpoint=Scalar("SELECT identity_json FROM endpoints WHERE endpoint_key=@p0 AND world_key=@p1",Key(source),r.Context.WorldKey) as byte[];
                        var known=endpoint==null?null:LedgerJson.Decode<LedgerEndpointState>(endpoint);
                        if(known!=null&&known.Identity!=null&&known.Identity.OwnerConsistent!=false&&
                            !Different(known.Identity.StructureEntityId,r.Identity.StructureEntityId)&&!Different(known.Identity.BlockGrid,r.Identity.BlockGrid)&&known.LastDialogue!=null&&known.LastDialogue.Dialogue!=null)
                        {r.Merchant=known.LastDialogue.Dialogue.TitleVerbatim;r.MerchantSource="same scoped observed NPC endpoint; owner/block evidence nonconflicting";bytes=LedgerJson.Encode(r);}
                    }
                    if(bytes.Length>32768)throw new InvalidDataException("trade-receipt-size");
                    Exec("INSERT INTO trades_v1(trade_id,world_key,player_id,observed_utc,receipt_json) VALUES(@p0,@p1,@p2,@p3,@p4)",r.TradeId,r.Context.WorldKey,r.PlayerId,r.Utc,bytes);
                    Inject("trade-before-commit");transaction.Commit();return true;
                }
            }
            finally{transaction=null;}
        }
    }
}
