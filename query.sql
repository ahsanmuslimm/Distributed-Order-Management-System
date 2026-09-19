SELECT COUNT(*) as total_orders, 
       SUM(CASE WHEN "Status" = 'Confirmed' THEN 1 ELSE 0 END) as confirmed_orders,
       SUM(CASE WHEN "Status" = 'Pending' THEN 1 ELSE 0 END) as pending_orders,
       SUM(CASE WHEN "Status" = 'Failed' THEN 1 ELSE 0 END) as failed_orders
FROM "Orders" 
WHERE "CreatedAt" > NOW() - INTERVAL '10 minutes';
