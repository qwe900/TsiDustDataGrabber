SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------
-- Table structure for tsi_dust
-- ----------------------------
DROP TABLE IF EXISTS `tsi_dust`;
CREATE TABLE `tsi_dust`  (
  `chartid` int NOT NULL AUTO_INCREMENT,
  `projectid` int NULL DEFAULT NULL,
  `name` text CHARACTER SET utf8 COLLATE utf8_general_ci NULL,
  `active` tinyint NULL DEFAULT NULL,
  `lat` float(16, 13) NULL DEFAULT NULL,
  `long` float(16, 13) NULL DEFAULT NULL,
  `lastdataset` datetime NULL DEFAULT NULL,
  `created` datetime NULL DEFAULT NULL,
  `updated` datetime NULL DEFAULT NULL,
  `serialnumber` varchar(255) CHARACTER SET utf8 COLLATE utf8_general_ci NULL DEFAULT NULL,
  PRIMARY KEY (`chartid`) USING BTREE
) ENGINE = MyISAM AUTO_INCREMENT = 10 CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = Dynamic;

SET FOREIGN_KEY_CHECKS = 1;
