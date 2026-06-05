using System;
using System.Collections.Generic;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.Edge
{
    public class BomBarCodeDao : Repository<bom_barcode>
    {

        public List<bom_barcode> GetListByStation(pms_station_no p)
        {
            List<bom_barcode> list = new List<bom_barcode>();
            list = base.AsQueryable()
                .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line && it.station_no == p.station_no)
                .ToList();
            return list;
        }

        public bom_barcode GetOneByStation(pms_station_no p)
        {
            bom_barcode one = new bom_barcode();
            one = base.AsQueryable()
                .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line && it.station_no == p.station_no)
                .First();
            return one;
        }

        public bom_barcode GetById(int id)
        {
            bom_barcode one = new bom_barcode();
            one = base.AsQueryable().Where(it => it.id == id).First();
            return one;
        }

        public bool Add(bom_barcode it)
        {
            bool ok = false;
            try
            {
                ok = base.Insert(it);
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return ok;
        }
        public bool Update(bom_barcode it)
        {
            bool ok = false;
            try
            {
                ok = base.Update(it);
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return ok;
        }

        public bool Del(bom_barcode it)
        {
            bool ok = false;
            try
            {
                ok = base.Delete(it);
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return ok;
        }
        public bool DelById(int id)
        {
            bool ok = false;
            try
            {
                ok = base.DeleteById(id);
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return ok;
        }

    }
}
